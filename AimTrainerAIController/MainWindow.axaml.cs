using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AimTrainerAIController
{
    public partial class MainWindow : Window
    {
        private FileSystemWatcher fileWatcher;
        private Process aimTrainerProcess;
        private CancellationTokenSource cancellationTokenSource;
        private double currentProgress = 0.0;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            GameSelector = this.FindControl<ComboBox>("GameSelector");
            ProgressBar = this.FindControl<ProgressBar>("ProgressBar");
            ProgressPercentage = this.FindControl<TextBlock>("ProgressPercentage");
            ConsoleOutput = this.FindControl<TextBox>("ConsoleOutput");
            StatusLabel = this.FindControl<TextBlock>("StatusLabel");
        }

        private void GameSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedGame = (e.AddedItems[0] as ComboBoxItem)?.Content.ToString().ToLower();
            if (!string.IsNullOrEmpty(selectedGame))
            {
                UpdateFileWatcher(selectedGame);
            }
        }

        private void UpdateFileWatcher(string game)
        {
            var videoDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "videos", game);
            if (fileWatcher != null)
            {
                fileWatcher.Dispose();
            }

            if (Directory.Exists(videoDirectory))
            {
                fileWatcher = new FileSystemWatcher(videoDirectory)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                fileWatcher.Changed += OnVideoFilesChanged;
                fileWatcher.Created += OnVideoFilesChanged;
                fileWatcher.Deleted += OnVideoFilesChanged;
                fileWatcher.EnableRaisingEvents = true;
            }
        }

        private void OnVideoFilesChanged(object sender, FileSystemEventArgs e)
        {
            // Handle file changes if needed
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (aimTrainerProcess == null)
            {
                string selectedGame = ((ComboBoxItem)GameSelector.SelectedItem)?.Content.ToString().ToLower();
                string videoFolder = Path.Combine("videos", selectedGame);

                if (!Directory.Exists(videoFolder))
                {
                    UpdateConsoleOutput($"Error: Video folder for {selectedGame} does not exist.");
                    return;
                }

                var videoFiles = Directory.GetFiles(videoFolder, "*.mp4");

                if (videoFiles.Length == 0)
                {
                    UpdateConsoleOutput($"Error: No video files found in {videoFolder}.");
                    return;
                }

                cancellationTokenSource = new CancellationTokenSource();

                aimTrainerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "AimTrainerAI.exe",
                        Arguments = $"{selectedGame} {string.Join(" ", videoFiles)}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                aimTrainerProcess.OutputDataReceived += (s, ea) =>
                {
                    if (ea.Data != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            ConsoleOutput.Text += $"{ea.Data}\n";
                            var progressText = GetProgressFromOutput(ea.Data);
                            ProgressPercentage.Text = $"Model Training: {progressText}";
                        });
                    }
                };

                aimTrainerProcess.ErrorDataReceived += (s, ea) =>
                {
                    if (ea.Data != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            ConsoleOutput.Text += $"ERROR: {ea.Data}\n";
                        });
                    }
                };

                aimTrainerProcess.Start();
                aimTrainerProcess.BeginOutputReadLine();
                aimTrainerProcess.BeginErrorReadLine();

                Task.Run(() => MonitorProgress(cancellationTokenSource.Token));
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (aimTrainerProcess != null && !aimTrainerProcess.HasExited)
            {
                aimTrainerProcess.Kill();
                aimTrainerProcess = null;
                cancellationTokenSource.Cancel();
                ConsoleOutput.Text += "Monitoring stopped.\n";
            }
        }

        private async Task MonitorProgress(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                double progress = currentProgress;
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressBar.Value = progress;
                    ProgressPercentage.Text = $"Model Training: {progress:0.00}%";
                });

                await Task.Delay(1000); // Adjust delay as needed
            }
        }

        private string GetProgressFromOutput(string output)
        {
            // Extract progress from output if available
            string pattern = @"Progress:\s*(\d+(\.\d+)?)%";
            Match match = Regex.Match(output, pattern);
            if (match.Success)
            {
                currentProgress = double.Parse(match.Groups[1].Value);
                return currentProgress.ToString("0.00");
            }
            return string.Empty;
        }

        private void UpdateConsoleOutput(string output)
        {
            if (output == null) return;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var consoleOutput = this.FindControl<TextBox>("ConsoleOutput");
                consoleOutput.Text += $"{output}\n";
                consoleOutput.CaretIndex = consoleOutput.Text.Length; // Auto-scroll to the bottom

                double progress = GetProgressFromOutput(output);
                var progressPercentage = this.FindControl<TextBlock>("ProgressPercentage");
                progressPercentage.Text = $"Model Training: {progress:0.00}%";
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
