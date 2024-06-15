using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AimTrainerAIController
{
    public partial class MainWindow : Window
    {
        private Process aimTrainerAIProcess;
        private CancellationTokenSource monitoringCancellationTokenSource;
        private List<string> videoPaths = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void AddVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Video",
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Video Files", Extensions = { "mp4", "avi", "mkv" } }
                },
                AllowMultiple = true
            };

            var result = await dialog.ShowAsync(this);
            if (result != null)
            {
                foreach (var path in result)
                {
                    videoPaths.Add(path);
                    var videoListBox = this.FindControl<ListBox>("VideoListBox");
                    videoListBox.Items = new List<string>(videoPaths); // Refresh the list
                }
            }
        }

        private void StartMonitoring_Click(object sender, RoutedEventArgs e)
        {
            var selectedGame = (this.FindControl<ComboBox>("GameSelectionComboBox").SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(selectedGame))
            {
                ShowMessage("Please select a game.");
                return;
            }

            if (videoPaths.Count == 0)
            {
                ShowMessage("Please add at least one video.");
                return;
            }

            if (monitoringCancellationTokenSource != null)
            {
                ShowMessage("Monitoring is already running.");
                return;
            }

            monitoringCancellationTokenSource = new CancellationTokenSource();
            var token = monitoringCancellationTokenSource.Token;

            Task.Run(() => StartAimTrainerAI(selectedGame, token), token);
        }

        private void StopMonitoring_Click(object sender, RoutedEventArgs e)
        {
            monitoringCancellationTokenSource?.Cancel();
            monitoringCancellationTokenSource = null;
            aimTrainerAIProcess?.Kill();
            aimTrainerAIProcess = null;
            ShowMessage("Stopped monitoring game processes.");
        }

        private async Task StartAimTrainerAI(string selectedGame, CancellationToken token)
        {
            var progressBar = this.FindControl<ProgressBar>("ProgressBar");
            var progressPercentage = this.FindControl<TextBlock>("ProgressPercentage");
            progressBar.Value = 0;

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AimTrainerAI.exe");
            if (!File.Exists(exePath))
            {
                ShowMessage("AimTrainerAI.exe not found.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"{selectedGame.ToLower()} {string.Join(" ", videoPaths)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            aimTrainerAIProcess = new Process { StartInfo = startInfo };

            aimTrainerAIProcess.OutputDataReceived += (sender, e) => UpdateConsoleOutput(e.Data);
            aimTrainerAIProcess.ErrorDataReceived += (sender, e) => UpdateConsoleOutput(e.Data);

            aimTrainerAIProcess.Start();
            aimTrainerAIProcess.BeginOutputReadLine();
            aimTrainerAIProcess.BeginErrorReadLine();

            while (!aimTrainerAIProcess.HasExited)
            {
                if (token.IsCancellationRequested)
                {
                    aimTrainerAIProcess.Kill();
                    break;
                }

                // Simulate progress update (in a real scenario, this would be updated based on actual processing progress)
                double progress = GetProgressFromOutput(); // Implement this method to read actual progress
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    progressBar.Value = progress;
                    progressPercentage.Text = $"Model Training: {progress:0.00}%";
                });

                await Task.Delay(100); // Adjust delay as needed
            }

            aimTrainerAIProcess?.WaitForExit();
            aimTrainerAIProcess = null;

            if (progressBar.Value >= 100)
            {
                ShowMessage("Model training completed.");
            }
        }

        private double GetProgressFromOutput()
        {
            // Placeholder for actual implementation to get progress from AimTrainerAI output
            return new Random().NextDouble() * 100;
        }

        private void UpdateConsoleOutput(string output)
        {
            if (output == null) return;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var consoleOutput = this.FindControl<TextBox>("ConsoleOutput");
                consoleOutput.Text += $"{output}\n";
                consoleOutput.CaretIndex = consoleOutput.Text.Length; // Auto-scroll to the bottom
            });
        }

        private void ShowMessage(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var statusLabel = this.FindControl<TextBlock>("StatusLabel");
                statusLabel.Text = message;

                var consoleOutput = this.FindControl<TextBox>("ConsoleOutput");
                consoleOutput.Text += $"{message}\n";
                consoleOutput.CaretIndex = consoleOutput.Text.Length; // Auto-scroll to the bottom
            });
        }
    }
}
