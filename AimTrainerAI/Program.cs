using Microsoft.ML;
using Microsoft.ML.Data;
using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AimTrainerAI
{
    class Program
    {
        private static readonly string[] SupportedGames = { "VALORANT.exe", "fortnite.exe", "cs2.exe" };
        private static MLContext mlContext;
        private static ITransformer model;
        private static List<TrainingData> trainingData;
        private static TemplateMatching templateMatching;
        private static PredictionEngine<TrainingData, AimPrediction> predictionEngine;
        private static CancellationTokenSource monitoringCancellationTokenSource;

        // Store crosshair positions with timestamps
        private static List<(int frameIndex, Point crosshairPosition)> crosshairHistory = new List<(int frameIndex, Point crosshairPosition)>();

        static async Task Main(string[] args)
        {
            mlContext = new MLContext();
            trainingData = new List<TrainingData>();

            Console.WriteLine("AimTrainerAI Console Application");
            Console.WriteLine("Commands: 'process <video_path>', 'start', 'stop', 'exit'");

            while (true)
            {
                var input = Console.ReadLine();
                var command = input.Split(' ');

                switch (command[0])
                {
                    case "process":
                        if (command.Length > 1)
                        {
                            await ProcessVideoAsync(command[1]);
                        }
                        else
                        {
                            Console.WriteLine("Usage: process <video_path>");
                        }
                        break;
                    case "start":
                        StartMonitoring();
                        break;
                    case "stop":
                        StopMonitoring();
                        break;
                    case "exit":
                        return;
                    default:
                        Console.WriteLine("Unknown command");
                        break;
                }
            }
        }

        private static async Task ProcessVideoAsync(string videoPath)
        {
            if (!File.Exists(videoPath))
            {
                Console.WriteLine("Invalid video path.");
                return;
            }

            Console.WriteLine("Processing video...");
            string gameName = DetectGameFromVideoPath(videoPath);
            string gameFolder = Path.Combine("assets", gameName);
            templateMatching = new TemplateMatching(gameFolder);

            await AnalyzeVideoAsync(videoPath);
            TrainModel();
            Console.WriteLine("Video processing and model training completed.");
        }

        private static string DetectGameFromVideoPath(string videoPath)
        {
            // Simple logic to detect game from video path, you can improve this
            if (videoPath.ToLower().Contains("valorant")) return "valorant";
            if (videoPath.ToLower().Contains("fortnite")) return "fortnite";
            if (videoPath.ToLower().Contains("cs2")) return "cs2";
            throw new InvalidOperationException("Unsupported game detected from video path.");
        }

        private static async Task AnalyzeVideoAsync(string videoPath)
        {
            var capture = new VideoCapture(videoPath);
            var totalFrames = capture.FrameCount;
            var frameStep = totalFrames / 100;

            for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
            {
                Mat frame = new Mat();
                capture.Read(frame);
                if (frame.Empty())
                    break;

                AnalyzeFrame(frame, frameIndex);

                if (frameIndex % frameStep == 0)
                {
                    Console.WriteLine($"Processed {frameIndex}/{totalFrames} frames.");
                }
            }

            capture.Release();
            Console.WriteLine("Video analysis completed.");
        }

        private static void AnalyzeFrame(Mat frame, int frameIndex)
        {
            Mat grayFrame = new Mat();
            Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);

            (bool crosshairDetected, Point crosshairPosition) = templateMatching.MatchTemplate(grayFrame, "crosshairs");
            (bool hitDetected, Point hitPosition) = templateMatching.MatchTemplate(grayFrame, "hit_markers");

            if (crosshairDetected)
            {
                // Store the detected crosshair position and frame index
                crosshairHistory.Add((frameIndex, crosshairPosition));
            }

            bool enemyDetected = DetectEnemyOutline(frame);

            bool hit = hitDetected && enemyDetected && IsPlayerHit(frameIndex, hitPosition);

            var data = new TrainingData
            {
                CrosshairX = crosshairPosition.X,
                CrosshairY = crosshairPosition.Y,
                Hit = hit
            };
            trainingData.Add(data);
        }

        private static bool DetectEnemyOutline(Mat frame)
        {
            string[] outlineCategories = { "enemy_outlines/default", "enemy_outlines/tritanopia", "enemy_outlines/deuteranopia", "enemy_outlines/protanopia" };

            foreach (var category in outlineCategories)
            {
                (bool detected, Point position) = templateMatching.MatchTemplate(frame, category);
                if (detected)
                {
                    return true;
                }
            }

            return false;
        }


        private static bool IsPlayerHit(int frameIndex, Point hitPosition)
        {
            // Define the time window (in frames) and distance threshold for considering a hit
            const int timeWindow = 10;
            const int distanceThreshold = 50;

            // Find crosshair positions within the time window
            var relevantPositions = crosshairHistory
                .Where(p => p.frameIndex >= frameIndex - timeWindow && p.frameIndex <= frameIndex)
                .Select(p => p.crosshairPosition)
                .ToList();

            // Check if any crosshair position is close enough to the hit position
            foreach (var position in relevantPositions)
            {
                double distance = Math.Sqrt(Math.Pow(position.X - hitPosition.X, 2) + Math.Pow(position.Y - hitPosition.Y, 2));
                if (distance <= distanceThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrainModel()
        {
            var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = mlContext.Transforms.Concatenate("Features", nameof(TrainingData.CrosshairX), nameof(TrainingData.CrosshairY))
                .Append(mlContext.Transforms.Conversion.ConvertType(nameof(TrainingData.Hit), outputKind: DataKind.Boolean))
                .Append(mlContext.Regression.Trainers.LbfgsPoissonRegression());

            model = pipeline.Fit(dataView);

            mlContext.Model.Save(model, dataView.Schema, "aim_model.zip");
            predictionEngine = mlContext.Model.CreatePredictionEngine<TrainingData, AimPrediction>(model);
        }

        private static void StartMonitoring()
        {
            if (monitoringCancellationTokenSource != null)
            {
                Console.WriteLine("Monitoring is already running.");
                return;
            }

            monitoringCancellationTokenSource = new CancellationTokenSource();
            var token = monitoringCancellationTokenSource.Token;

            Task.Run(() => MonitorGameProcesses(token), token);
            Console.WriteLine("Started monitoring game processes.");
        }

        private static void StopMonitoring()
        {
            monitoringCancellationTokenSource?.Cancel();
            monitoringCancellationTokenSource = null;
            Console.WriteLine("Stopped monitoring game processes.");
        }

        private static async Task MonitorGameProcesses(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var activeGame = GetActiveGameProcess();
                if (activeGame != null)
                {
                    string gameName = Path.GetFileNameWithoutExtension(activeGame.MainModule.FileName).ToLower();
                    string gameFolder = Path.Combine("assets", gameName);

                    if (Directory.Exists(gameFolder))
                    {
                        templateMatching = new TemplateMatching(gameFolder);
                        Console.WriteLine($"Detected {gameName}. Starting video analysis...");

                        // Simulate real-time video capture and analysis
                        await SimulateGameVideoAnalysis(activeGame.ProcessName);

                        Console.WriteLine("Video analysis completed.");
                    }
                }

                await Task.Delay(1000); // Check every second
            }
        }

        private static Process GetActiveGameProcess()
        {
            var processes = Process.GetProcesses();
            return processes.FirstOrDefault(p => SupportedGames.Contains(p.ProcessName.ToLower() + ".exe"));
        }

        private static async Task SimulateGameVideoAnalysis(string gameName)
        {
            // Simulate video analysis by reading frames from a file or stream
            // This should be replaced with actual game screen capturing in a real implementation
            string videoPath = Path.Combine("sample_videos", $"{gameName}.mp4");
            if (!File.Exists(videoPath)) return;

            var capture = new VideoCapture(videoPath);
            var totalFrames = capture.FrameCount;
            var frameStep = totalFrames / 100;

            for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
            {
                Mat frame = new Mat();
                capture.Read(frame);
                if (frame.Empty())
                    break;

                AnalyzeFrame(frame, frameIndex);

                if (frameIndex % frameStep == 0)
                {
                    Console.WriteLine($"Processed {frameIndex}/{totalFrames} frames.");
                }
            }

            capture.Release();
        }
    }

    public class TrainingData
    {
        public int CrosshairX { get; set; }
        public int CrosshairY { get; set; }
        public bool Hit { get; set; }
    }

    public class AimPrediction
    {
        [ColumnName("Score")]
        public float PredictedHit { get; set; }
    }

    public class TemplateMatching
    {
        private Dictionary<string, List<Mat>> templates;

        public TemplateMatching(string gameFolder)
        {
            templates = new Dictionary<string, List<Mat>>();
            LoadTemplates(gameFolder);
        }

        private void LoadTemplates(string gameFolder)
        {
            string[] categories = { "crosshairs", "hit_vfx", "bullet_traces", "hit_markers", "enemy_outlines/default", "enemy_outlines/tritanopia", "enemy_outlines/deuteranopia", "enemy_outlines/protanopia" };
            foreach (var category in categories)
            {
                var categoryPath = Path.Combine(gameFolder, category);
                if (Directory.Exists(categoryPath))
                {
                    templates[category] = new List<Mat>();
                    foreach (var file in Directory.GetFiles(categoryPath))
                    {
                        templates[category].Add(Cv2.ImRead(file, ImreadModes.Grayscale));
                    }
                }
            }
        }

        public (bool, Point) MatchTemplate(Mat frame, string category)
        {
            if (!templates.ContainsKey(category)) return (false, new Point());

            foreach (var template in templates[category])
            {
                Mat result = new Mat();
                Cv2.MatchTemplate(frame, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);

                if (maxVal > 0.8) // Adjust threshold as needed
                {
                    return (true, maxLoc);
                }
            }

            return (false, new Point());
        }
    }

}
