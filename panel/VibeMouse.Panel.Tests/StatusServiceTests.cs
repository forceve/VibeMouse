using System.Text.Json.Nodes;
using VibeMouse.Panel.Services;
using Xunit;

namespace VibeMouse.Panel.Tests;

public class StatusServiceTests
{
    [Fact]
    public void FromJson_UsesReportedListenerStateAndDetails()
    {
        var snapshot = StatusSnapshot.FromJson(
            JsonNode.Parse(
                """
                {
                  "state": "idle",
                  "listener_mode": "child",
                  "listener_state": "crashed",
                  "listener_pid": 4321,
                  "listener_last_error": "listener child exited with code 7",
                  "last_transcript": "hello world",
                  "ipc_socket": "/tmp/vibemouse.sock"
                }
                """
            )!.AsObject()
        );

        Assert.Equal("idle", snapshot.State);
        Assert.Equal("child", snapshot.ListenerMode);
        Assert.Equal("crashed", snapshot.ListenerState);
        Assert.Equal(4321, snapshot.ListenerPid);
        Assert.Equal("listener child exited with code 7", snapshot.ListenerLastError);
        Assert.Equal("hello world", snapshot.LastTranscript);
        Assert.Equal("/tmp/vibemouse.sock", snapshot.IpcSocket);
        Assert.True(snapshot.IpcAvailable);
    }

    [Theory]
    [InlineData("idle", "inline", "running")]
    [InlineData("idle", "child", "running")]
    [InlineData("idle", "off", "disabled")]
    [InlineData("idle", "unknown", "unknown")]
    [InlineData("offline", "child", "offline")]
    public void ListenerState_FallsBackToModeWhenStatusFieldMissing(
        string state,
        string listenerMode,
        string expected)
    {
        var snapshot = new StatusSnapshot(state, listenerMode, string.Empty, null, null);

        Assert.Equal(expected, snapshot.ListenerState);
    }
}
