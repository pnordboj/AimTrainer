using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AimTrainerAIController
{
    public partial class MainWindow : Window
    {
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
            if (Directory.Exists(videoDirectory))
            {
                // No need to watch for file changes in this implementation
            }
            else
            {
                ConsoleOutput.Text += $"Error: Video folder for {game} does not exist.\n";
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (aimTrainerProcess == null)
            {
                string selectedGame = ((ComboBoxItem)GameSelector.SelectedItem).Content.ToString().ToLower();
                string videoFolder = Path.Combine("videos", selectedGame);

                if (!Directory.Exists(videoFolder))
                {
                    ConsoleOutput.Text += $"Error: Video folder for {selectedGame} does not exist.\n";
                    return;
                }

                var videoFiles = Directory.GetFiles(videoFolder, "*.mp4");

                if (videoFiles.Length == 0)
                {
                    ConsoleOutput.Text += $"Error: No video files found in {videoFolder}.\n";
                    return;
                }

                cancellationTokenSource = new CancellationTokenSource();

                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AimTrainerAI.exe");
                if (!File.Exists(exePath))
                {
                    ConsoleOutput.Text += "Error: AimTrainerAI.exe not found.\n";
                    return;
                }

                aimTrainerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
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
                            ProgressText.Text = GetProgressFromOutput(ea.Data);
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
                // Simulate progress update
                await Task.Delay(1000);
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressBar.Value += 1;
                    if (ProgressBar.Value > 100)
                    {
                        ProgressBar.Value = 0;
                    }
                });
            }
        }

        private string GetProgressFromOutput(string output)
        {
            // Extract progress from output if available
            if (output.Contains("Processed"))
            {
                var parts = output.Split(' ');
                if (parts.Length > 1 && int.TryParse(parts[1], out int progress))
                {
                    return $"Progress: {progress}%";
                }
            }
            return string.Empty;
        }
    }
}
