using Microsoft.ML;
using Microsoft.ML.Data;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Device = SharpDX.Direct3D11.Device;

namespace AimTrainerAI
{
    class Program
    {
        private static readonly string[] SupportedGames = { "VALORANT", "FortniteClient-Win64-Shipping", "cs2" };
        private static MLContext mlContext;
        private static ITransformer model;
        private static List<TrainingData> trainingData;
        private static TemplateMatching templateMatching;
        private static PredictionEngine<TrainingData, AimPrediction> predictionEngine;
        private static CancellationTokenSource monitoringCancellationTokenSource;
        private ScreenCapture screenCapture;

        // Store crosshair positions with timestamps
        private static List<(int frameIndex, Point crosshairPosition)> crosshairHistory = new List<(int frameIndex, Point crosshairPosition)>();

        static async Task Main(string[] args)
        {
            var program = new Program();
            program.Run(args);
        }

        private void Run(string[] args)
        {
            mlContext = new MLContext();
            trainingData = new List<TrainingData>();
            screenCapture = new ScreenCapture();

            Console.WriteLine("AimTrainerAI Console Application");
            Console.WriteLine("Commands: 'process <video_path>', 'start', 'stop', 'exit'");

            if (args.Length > 0)
            {
                string selectedGame = args[0].ToLower();
                if (SupportedGames.Contains(selectedGame))
                {
                    StartMonitoring(selectedGame).Wait();
                }
                else
                {
                    Console.WriteLine("Unsupported game.");
                }
            }
            else
            {
                while (true)
                {
                    var input = Console.ReadLine();
                    var command = input.Split(' ');

                    switch (command[0])
                    {
                        case "process":
                            if (command.Length > 1)
                            {
                                ProcessVideoAsync(command[1]).Wait();
                            }
                            else
                            {
                                Console.WriteLine("Usage: process <video_path>");
                            }
                            break;
                        case "start":
                            if (command.Length > 1)
                            {
                                StartMonitoring(command[1]).Wait();
                            }
                            else
                            {
                                Console.WriteLine("Usage: start <game_name>");
                            }
                            break;
                        case "stop":
                            StopMonitoring();
                            break;
                        case "exit":
                            screenCapture.Dispose();
                            return;
                        default:
                            Console.WriteLine("Unknown command");
                            break;
                    }
                }
            }
        }

        private async Task StartMonitoring(string selectedGame)
        {
            monitoringCancellationTokenSource = new CancellationTokenSource();
            var token = monitoringCancellationTokenSource.Token;

            await MonitorGameProcesses(selectedGame, token);
            Console.WriteLine("Started monitoring game processes.");
        }

        private void StopMonitoring()
        {
            monitoringCancellationTokenSource?.Cancel();
            monitoringCancellationTokenSource = null;
            Console.WriteLine("Stopped monitoring game processes.");
        }

        private async Task ProcessVideoAsync(string videoPath)
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

        private string DetectGameFromVideoPath(string videoPath)
        {
            // Simple logic to detect game from video path, you can improve this
            if (videoPath.ToLower().Contains("valorant")) return "valorant";
            if (videoPath.ToLower().Contains("fortnite")) return "fortnite";
            if (videoPath.ToLower().Contains("cs2")) return "cs2";
            throw new InvalidOperationException("Unsupported game detected from video path.");
        }

        private async Task AnalyzeVideoAsync(string videoPath)
        {
            var capture = new VideoCapture(videoPath);
            var totalFrames = capture.FrameCount;
            var frameStep = totalFrames / 100;

            for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
            {
                capture.Read(out var frame);
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

        private void AnalyzeFrame(Mat frame, int frameIndex)
        {
            Mat grayFrame = new Mat();
            Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);

            (bool crosshairDetected, Point crosshairPosition) = templateMatching.MatchTemplate(grayFrame, "crosshairs");
            (bool hitDetected, Point hitPosition) = templateMatching.MatchTemplate(grayFrame, "hit_markers");

            bool hit = hitDetected && IsPlayerHit(frameIndex, hitPosition);

            var data = new TrainingData
            {
                CrosshairX = crosshairPosition.X,
                CrosshairY = crosshairPosition.Y,
                Hit = hit
            };
            trainingData.Add(data);
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

        private void TrainModel()
        {
            var dataView = mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = mlContext.Transforms.Concatenate("Features", nameof(TrainingData.CrosshairX), nameof(TrainingData.CrosshairY))
                .Append(mlContext.Transforms.Conversion.ConvertType(nameof(TrainingData.Hit), outputKind: DataKind.Boolean))
                .Append(mlContext.Regression.Trainers.LbfgsPoissonRegression());

            model = pipeline.Fit(dataView);

            mlContext.Model.Save(model, dataView.Schema, "aim_model.zip");
            predictionEngine = mlContext.Model.CreatePredictionEngine<TrainingData, AimPrediction>(model);
        }

        private async Task MonitorGameProcesses(string selectedGame, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var activeGame = GetActiveGameProcess();
                if (activeGame != null)
                {
                    string gameFolder = Path.Combine("assets", selectedGame);

                    if (Directory.Exists(gameFolder))
                    {
                        templateMatching = new TemplateMatching(gameFolder);
                        Console.WriteLine($"Detected {selectedGame}. Starting real-time analysis...");

                        await CaptureAndAnalyzeGameFrames(activeGame, token);

                        Console.WriteLine("Real-time analysis completed.");
                    }
                }

                await Task.Delay(1000); // Check every second
            }
        }

        private Process GetActiveGameProcess()
        {
            var processes = Process.GetProcesses();
            Console.WriteLine("Active processes:");
            foreach (var process in processes)
            {
                Console.WriteLine(process.ProcessName.ToLower());
            }

            var activeGame = processes.FirstOrDefault(p => SupportedGames.Contains(p.ProcessName.ToLower()));
            if (activeGame != null)
            {
                Console.WriteLine($"Detected active game process: {activeGame.ProcessName}");
            }
            else
            {
                Console.WriteLine("No supported game process found.");
            }
            return activeGame;
        }

        private async Task CaptureAndAnalyzeGameFrames(Process gameProcess, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Capture game screen
                var frame = screenCapture.CaptureScreen();
                if (frame != null)
                {
                    AnalyzeFrame(frame, 0); // You can implement better frame indexing if needed
                }
                await Task.Delay(33); // Capture frame every ~30ms (30 FPS)
            }
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
            string[] categories = { "crosshairs", "hit_vfx", "bullet_traces", "hit_markers" };
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

    public class ScreenCapture : IDisposable
    {
        private Device device;
        private OutputDuplication outputDuplication;
        private Texture2DDescription textureDesc;
        private Texture2D screenTexture;

        public ScreenCapture()
        {
            var factory = new Factory1();
            var adapter = factory.GetAdapter1(0);
            var output = adapter.Outputs[0];
            var output1 = output.QueryInterface<Output1>();

            device = new Device(adapter);

            textureDesc = new Texture2DDescription
            {
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = output.Description.DesktopBounds.Right - output.Description.DesktopBounds.Left,
                Height = output.Description.DesktopBounds.Bottom - output.Description.DesktopBounds.Top,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Staging
            };

            screenTexture = new Texture2D(device, textureDesc);
            outputDuplication = output1.DuplicateOutput(device);
        }

        public Mat CaptureScreen()
        {
            SharpDX.DXGI.Resource screenResource;
            OutputDuplicateFrameInformation duplicateFrameInformation;

            // Remove the 'out' keyword from screenResource
            outputDuplication.AcquireNextFrame(1000, out duplicateFrameInformation, ref screenResource);

            using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
            {
                device.ImmediateContext.CopyResource(screenTexture2D, screenTexture);
            }

            var mapSource = device.ImmediateContext.MapSubresource(screenTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

            var mat = new Mat(textureDesc.Height, textureDesc.Width, MatType.CV_8UC4, mapSource.DataPointer, mapSource.RowPitch);

            device.ImmediateContext.UnmapSubresource(screenTexture, 0);
            screenResource.Dispose();

            return mat;
        }

        public void Dispose()
        {
            outputDuplication.Dispose();
            screenTexture.Dispose();
            device.Dispose();
        }
    }
}
