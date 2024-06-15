using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.ML;
using Microsoft.ML.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AimTrainer
{
    public partial class MainWindow : Window
    {
        private Random random;
        private int targetX, targetY;
        private double sensitivity;
        private List<TestData> testData;
        private PredictionEngine<TestData, SensitivityPrediction> predictionEngine;

        public MainWindow()
        {
            InitializeComponent();
            random = new Random();
            testData = new List<TestData>();
            LoadAndDisplayResults();
            LoadPredictionModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void StartTestButton_Click(object sender, RoutedEventArgs e)
        {
            SetNewTarget();
        }

        private void SetNewTarget()
        {
            var canvas = this.FindControl<Canvas>("TargetArea");
            targetX = random.Next(0, (int)canvas.Bounds.Width);
            targetY = random.Next(0, (int)canvas.Bounds.Height);
            canvas.Children.Clear();
            var target = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(target, targetX - 10);
            Canvas.SetTop(target, targetY - 10);
            canvas.Children.Add(target);
        }

        private void TargetArea_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            var canvas = sender as Canvas;
            var position = e.GetPosition(canvas);

            int distance = CalculateDistance((int)position.X, (int)position.Y, targetX, targetY);
            double dpi = double.Parse(DpiTextBox.Text);
            sensitivity = CalculateSensitivity(dpi, distance);

            ResultsLabel.Text = $"Distance: {distance}, Sensitivity: {sensitivity:F2}";
            TipsLabel.Text = GetPlacementTips(distance);

            SaveTestData((int)position.X, (int)position.Y, targetX, targetY, distance);
            SaveResults(dpi, sensitivity);
        }

        private int CalculateDistance(int x1, int y1, int x2, int y2)
        {
            return (int)Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        }

        private double CalculateSensitivity(double dpi, int distance)
        {
            return dpi / distance;
        }

        private string GetPlacementTips(int distance)
        {
            if (distance < 50)
                return "Great job! Keep it up!";
            else if (distance < 100)
                return "Good, but you can improve.";
            else
                return "Practice more to improve accuracy.";
        }

        private void SaveTestData(int clickX, int clickY, int targetX, int targetY, int distance)
        {
            var data = new TestData
            {
                ClickX = clickX,
                ClickY = clickY,
                TargetX = targetX,
                TargetY = targetY,
                Distance = distance,
                Timestamp = DateTime.Now
            };
            testData.Add(data);
            SaveTestDataToFile();
        }

        private void SaveTestDataToFile()
        {
            var json = JsonConvert.SerializeObject(testData, Formatting.Indented);
            File.WriteAllText("testdata.json", json);
        }

        private void SaveResults(double dpi, double sensitivity)
        {
            var results = LoadResults();
            var newResult = new AimResult
            {
                Username = UsernameTextBox.Text,
                Game = GameTextBox.Text,
                Dpi = dpi,
                Sensitivity = sensitivity,
                Date = DateTime.Now,
                RecommendedSensitivity = GetRecommendedSensitivity(dpi, sensitivity)
            };

            results.Add(newResult);
            SaveResultsToFile(results);
            LoadAndDisplayResults();
        }

        private List<AimResult> LoadResults()
        {
            if (File.Exists("results.json"))
            {
                var json = File.ReadAllText("results.json");
                return JsonConvert.DeserializeObject<List<AimResult>>(json) ?? new List<AimResult>();
            }
            return new List<AimResult>();
        }

        private void SaveResultsToFile(List<AimResult> results)
        {
            var json = JsonConvert.SerializeObject(results, Formatting.Indented);
            File.WriteAllText("results.json", json);
        }

        private double GetRecommendedSensitivity(double dpi, double baseSensitivity)
        {
            var data = new TestData { Distance = (int)(dpi / baseSensitivity) };
            var prediction = predictionEngine.Predict(data);
            return prediction.Sensitivity;
        }

        private void LoadAndDisplayResults()
        {
            var resultsListBox = this.FindControl<ListBox>("ResultsListBox");
            var results = LoadResults();
            resultsListBox.Items = results;
        }

        private void LoadPredictionModel()
        {
            var mlContext = new MLContext();
            DataViewSchema modelSchema;
            var model = mlContext.Model.Load("model.zip", out modelSchema);
            predictionEngine = mlContext.Model.CreatePredictionEngine<TestData, SensitivityPrediction>(model);
        }

        public class TestData
        {
            public int ClickX { get; set; }
            public int ClickY { get; set; }
            public int TargetX { get; set; }
            public int TargetY { get; set; }
            public int Distance { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class AimResult
        {
            public string Username { get; set; }
            public string Game { get; set; }
            public double Dpi { get; set; }
            public double Sensitivity { get; set; }
            public DateTime Date { get; set; }
            public double RecommendedSensitivity { get; set; }

            public override string ToString()
            {
                return $"{Date}: {Username} - {Game} - DPI: {Dpi} - Sensitivity: {Sensitivity:F2} - Recommended: {RecommendedSensitivity:F2}";
            }
        }

        public class SensitivityPrediction
        {
            [ColumnName("Score")]
            public float Sensitivity { get; set; }
        }
    }
}
