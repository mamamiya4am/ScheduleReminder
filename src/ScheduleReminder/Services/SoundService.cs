using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using ScheduleReminder.Domain;

namespace ScheduleReminder.Services
{
    /// <summary>提醒声音服务接口（Play / Stop / Preview / Configure）。</summary>
    public interface ISoundService
    {
        void Play();

        /// <summary>提醒播放：按这条日程自己选的音效名播放（null / "-" = 不响）。</summary>
        void Play(string soundName);

        void Stop();

        /// <summary>试听某个音效名（不影响提醒时的选择）；传 null / "-" 表示不播放。</summary>
        void Preview(string soundName);

        /// <summary>只停止"试听"发出的声音；若当前正在响的是提醒声则不动（避免误伤）。</summary>
        void StopPreview();

        void Configure(string soundName);
    }

    /// <summary>
    /// 声音服务：播放 data\sound\ 下用户选定的音效（WAV / MP3，MediaPlayer 同时支持且带 Volume）。
    /// - 选定某个音效 → 播放它；
    /// - 选「-」或什么都没选 → **完全静音**（提醒照常弹，只是不响）；
    /// - 选定的音效文件被用户删掉了 → 静音 + 记日志，**不崩溃、不弹错**；
    /// - 播放结束 / 失败立即释放 MediaPlayer，避免长期占用音频文件。
    /// </summary>
    public sealed class SoundService : ISoundService
    {        
        private const double MaxPlaySeconds = 3.0;
        private MediaPlayer _player;
        private DispatcherTimer _limitTimer;
        private bool _previewPlaying; // 当前播放的是"试听"还是"提醒"
        private double _volume = 1.0;
        private string _soundName;

        /// <summary>开始播放后启动"最长时长"计时器，到点停止（对提醒与试听一视同仁）。</summary>
        private void StartLimitTimer()
        {
            if (_limitTimer == null)
            {
                _limitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MaxPlaySeconds) };
                _limitTimer.Tick += OnPlayLimitReached;
            }
            _limitTimer.Stop();
            _limitTimer.Start();
        }

        private void StopLimitTimer()
        {
            if (_limitTimer != null)
            {
                _limitTimer.Stop();
            }
        }

        private void OnPlayLimitReached(object sender, EventArgs e)
        {
            StopLimitTimer();
            Stop();
        }

        public void Configure(string soundName)
        {
            _soundName = soundName;
        }

        public void Play()
        {
            _previewPlaying = false;
            PlayCore(_soundName);
        }

        /// <summary>提醒播放：播这条日程自己选的音效（null / "-" → 静音）。</summary>
        public void Play(string soundName)
        {
            _previewPlaying = false;
            PlayCore(soundName);
        }

        /// <summary>试听：按名字播放一次，方便用户在设置里挑音效（与提醒播放互不影响）。</summary>
        public void Preview(string soundName)
        {
            _previewPlaying = true;
            PlayCore(soundName);
        }

        /// <summary>只停止试听的声音；正在响的是提醒声则保持不动。</summary>
        public void StopPreview()
        {
            if (_previewPlaying)
            {
                Stop();
            }
        }

        private void PlayCore(string soundName)
        {
            string full = SoundLibrary.ResolvePath(soundName);
            if (full == null)
            {
                // 没选 / 选了「-」/ 文件被删 → 静音，不播放任何声音
                return;
            }

            try
            {
                Stop();
                MediaPlayer player = new MediaPlayer();
                player.Volume = _volume;
                // 播完或失败都释放：否则音频文件会被 MediaPlayer 一直占用（用户删不掉、替换不了）
                player.MediaEnded += delegate { Release(player); };
                player.MediaFailed += delegate { Release(player); };
                player.Open(new Uri(full, UriKind.Absolute));
                player.Play();
                _player = player;
                StartLimitTimer(); // 无论原音频多长，最多播 MaxPlaySeconds 秒
            }
            catch (Exception ex)
            {
                LogWriter.Write("播放音效失败，本次静音：" + full, ex);
                Stop();
            }
        }

        private void Release(MediaPlayer player)
        {
            try
            {
                if (!ReferenceEquals(player, _player))
                {
                    // 已经被后来的提醒替换掉了，仍然要关掉这个旧实例
                    player.Stop();
                    player.Close();
                    return;
                }
                player.Stop();
                player.Close();
                _player = null;
            }
            catch (Exception ex)
            {
                LogWriter.Write("释放播放器失败", ex);
            }
        }

        public void Stop()
        {
            StopLimitTimer();
            if (_player == null)
            {
                return;
            }
            try
            {
                _player.Stop();
                _player.Close();
            }
            catch (Exception ex)
            {
                LogWriter.Write("停止声音失败", ex);
            }
            finally
            {
                _player = null;
            }
        }
    }
}
