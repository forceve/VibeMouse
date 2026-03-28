using System;
using System.IO;
using System.Threading.Tasks;
using VibeMouse.Panel.Services;
using VibeMouse.Panel.ViewModels;
using Xunit;

namespace VibeMouse.Panel.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;
    private readonly string _statusPath;

    public MainViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vibemouse-panel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _statusPath = Path.Combine(_tempDir, "status.json");
        File.WriteAllText(_configPath, "{}", System.Text.Encoding.UTF8);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ReloadConfigAsync_SendsReloadConfigCommand()
    {
        var (viewModel, control) = CreateSubject();

        viewModel.ApplyStatusSnapshot(new StatusSnapshot("idle", "inline", "", "test://ipc", null));
        await viewModel.ReloadConfigAsync();

        Assert.Equal("reload_config", control.LastCommand);
        viewModel.Dispose();
    }

    [Fact]
    public async Task RunDoctorAsync_InvokesDoctorRunner()
    {
        var (viewModel, control) = CreateSubject();

        viewModel.ApplyStatusSnapshot(new StatusSnapshot("idle", "inline", "", "test://ipc", null));
        await viewModel.RunDoctorAsync();

        Assert.True(control.DoctorRan);
        viewModel.Dispose();
    }

    private (MainViewModel ViewModel, RecordingAgentControlService Control) CreateSubject()
    {
        var configService = new ConfigService(_configPath);
        var statusService = new StatusService(_statusPath);
        var control = new RecordingAgentControlService(statusService);
        var viewModel = new MainViewModel(configService, statusService, control, startStatusPolling: false);
        return (viewModel, control);
    }

    private sealed class RecordingAgentControlService(StatusService statusService)
        : AgentControlService(statusService)
    {
        public string? LastCommand { get; private set; }
        public bool DoctorRan { get; private set; }

        public override Task SendCommandAsync(string command)
        {
            LastCommand = command;
            return Task.CompletedTask;
        }

        public override Task RunDoctorAsync()
        {
            DoctorRan = true;
            return Task.CompletedTask;
        }
    }
}
