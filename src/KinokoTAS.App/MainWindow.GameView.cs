using System;
using KinokoTAS.Core;

namespace KinokoTAS.App;

public partial class MainWindow
{
    public void RefreshGameView()
    {
        if (!ReferenceEquals(_playbackSession, _game))
        {
            _playbackSession = _game;
            _playbackState = null;
        }

        BindLayoutProject();
        CancelOperationButton.IsEnabled = _seeking is not null;
        RefreshEditWorkflow();
        RestoreOverwriteButton.IsEnabled = !_gameCommand && !_busy && _game is not null && _recoveries.Count > 0;
        RestartGameButton.IsEnabled = !_gameCommand && !_busy && Project?.InvalidFrom is null &&
                                      (!_game?.IsRunning ?? Project is not null);
        RestartGameButton.Label = "重新启动游戏";
        if (_game is null)
        {
            UpdatePlaybackButtons(null);
            return;
        }

        if (!_game.IsRunning)
        {
            _playbackState = null;
            UpdatePlaybackButtons(null);
            EngineLabel.Text = "游戏已关闭";
            GameStatus.Text = _operationError ?? "点击“重新启动游戏”恢复当前录制。";
            return;
        }

        try
        {
            if (_game.ExternalWindow && OperatingSystem.IsWindows())
            {
                if (_orderedProcess != _game.GameProcessId)
                {
                    _orderedProcess = _game.GameProcessId;
                    _orderedGameWindow = 0;
                }

                var handle = _game.GameWindowHandle;
                if (handle != 0 && handle != _orderedGameWindow &&
                    GameWindowOrder.Attach(handle, TryGetPlatformHandle()?.Handle ?? 0))
                    _orderedGameWindow = handle;
            }

            _game.Input(CurrentMask());
            var state = _game.ReadState();
            if (state is not null)
                _playbackState = state;

            UpdatePlaybackButtons(_playbackState);
            if (state is null)
                return;

            RefreshTimeline(state);
            RecordToggle.IsChecked = _game.IsLive;
            _preview.Show(_game.ExternalWindow ? null : _game.ReadPreview());

            var phase = state.Phase switch
            {
                "paused" or "live-paused" => "已暂停",
                "live" => "正在录制",
                "playing" => "正在回放",
                "finished" => "已结束",
                "failed" => "运行失败",
                _ => "正在启动"
            };
            EngineLabel.Text = $"{phase} · 已完成 {state.Completed} 帧";
            FrameLabel.Text = Math.Max(0, state.Completed - 1).ToString("D6");
            FrameDetails.Text = $"时间 {Math.Max(0, state.Completed - 1) / 60.0:F2} 秒";
            if (_seeking is not null)
            {
                GameStatus.Text = _operationProgress ?? $"正在定位：{state.Completed:N0} / {_seekTarget:N0} 帧（Esc 取消）";
                return;
            }

            if (_operationError is not null)
            {
                GameStatus.Text = _operationError;
                return;
            }

            if (_game.ExternalWindow)
            {
                GameStatus.Text = $"{phase} · 在独立游戏窗口操作，F9 播放/暂停，F10 前进一帧。切换窗口不会自动暂停。";
                return;
            }

            GameStatus.Text = $"画面帧 {_preview.Completed - 1} / 逻辑帧 {state.Completed - 1} · " + (_game.IsLive
                ? "接管输入：方向键、Z 跳跃/确认、X 攻击/加速/搬运、A 暂停、C 道具、F10 执行一帧。点击画面获取焦点。"
                : "双击时间轴帧号或使用定位按钮查看。回退会从头重播，请等待。");
        }
        catch (Exception ex)
        {
            EngineLabel.Text = "引擎错误";
            GameStatus.Text = ex.Message;
        }
    }

    private void UpdatePlaybackButtons(SessionState? state)
    {
        var running = state?.Phase is "playing" or "live";
        PlayGameButton.IsVisible = !running;
        PauseGameButton.IsVisible = running;
        PlayGameButton.IsEnabled = _game is { IsRunning: true }
                                   && state is { Phase: "paused" or "live-paused" }
                                   && (_game.IsLive || state.Completed < state.Total)
                                   && !_gameCommand && !_busy && _seeking is null
                                   && Project?.InvalidFrom is null;
        PauseGameButton.IsEnabled = running;
    }
}
