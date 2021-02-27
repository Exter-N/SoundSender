﻿using EtherSound.Properties;
using EtherSound.Settings;
using EtherSound.View;
using EtherSound.ViewModel;
using EtherSound.WebSocket;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using WASCap;
using Application = System.Windows.Forms.Application;
using MessageBox = System.Windows.Forms.MessageBox;
using Timer = System.Windows.Forms.Timer;

namespace EtherSound
{
    class Program : IDisposable
    {
        static Dispatcher dispatcher;
        static int suspendSaving = 0;

        readonly SettingsContainer settings;
        readonly RootModel viewModel;
        readonly SessionManager sessions;
        readonly Server wsServer;
        bool ready;
        readonly Timer saveTimer;
        readonly Timer cursorTimer;
        readonly Timer pollTimer;
        readonly TrayIcon trayIcon;
        VolumeControlWindow vcWindow;
        SettingsWindow stWindow;

        public static Dispatcher Dispatcher => dispatcher;

        public Program(string settingsPath)
        {
            settings = new SettingsContainer(settingsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EtherSound", "Settings.json"));
            viewModel = new RootModel(settings.Settings);
            sessions = new SessionManager(settings.Settings);
            wsServer = new Server(viewModel, sessions, settings.Settings.WebSocketEndpoint);
            ready = false;

            SetTimer(out saveTimer, 3000, delegate { if (ready && suspendSaving == 0) { viewModel.UpdateSettings(); settings.Save(); } });
            SetTimer(out cursorTimer, 1000, delegate { viewModel.UpdateCursor(); });
            SetTimer(out pollTimer, 5, delegate { viewModel.Poll(); });

            trayIcon = new TrayIcon(viewModel);

            viewModel.PropertyChanged += ModelPropertyChanged;
            trayIcon.VolumeControlClicked += delegate { OpenVolumeControlWindow(); };
            trayIcon.SettingsClicked += delegate { OpenSettingsWindow(); };
            trayIcon.ResetClicked += async delegate { await sessions.RefreshDevices().ConfigureAwait(true); sessions.RestartAll(); };
        }

        async Task LoadSettings()
        {
            await sessions.RefreshDevices().ConfigureAwait(true);
            foreach (SessionSettings sSettings in settings.Settings.Sessions)
            {
                viewModel.AddSession(CreateSessionModel(sSettings));
            }
            ready = true;
        }

        public static SessionModel CreateSessionModel(SessionSettings settings)
        {
            return new SessionModel(
                settings,
                new ControlStructure("EtherSound-" + Guid.NewGuid()),
                new BufferConsole(20));
        }

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            dispatcher = Dispatcher.CurrentDispatcher;
            using (Program p = new Program((args.Length > 0) ? args[0] : null))
            {
                Queue(p.LoadSettings);
                Application.Run();
            }
        }

        public static async void Queue(Action action)
        {
            await dispatcher.InvokeAsync(() => action());
        }

        public static async void Queue<T>(Func<T> action)
        {
            await dispatcher.InvokeAsync(() => action());
        }

        public static async void Queue<T>(Action<T> action, T parameter)
        {
            await dispatcher.InvokeAsync(() => action(parameter));
        }

        public static async void Queue<T, U>(Func<T, U> action, T parameter)
        {
            await dispatcher.InvokeAsync(() => action(parameter));
        }

        public static void SuspendSavingSettings()
        {
            Interlocked.Increment(ref suspendSaving);
        }

        public static void ResumeSavingSettings()
        {
            if (Interlocked.Decrement(ref suspendSaving) < 0)
            {
                throw new InvalidOperationException();
            }
        }

        public void Dispose()
        {
            ready = false;
            wsServer.Dispose();
            sessions.Dispose();
            trayIcon.Dispose();
            pollTimer.Stop();
            pollTimer.Dispose();
            cursorTimer.Stop();
            cursorTimer.Dispose();
            saveTimer.Stop();
            saveTimer.Dispose();
        }

        static void SetTimer(out Timer timer, int interval, EventHandler tick)
        {
            timer = new Timer
            {
                Interval = interval
            };
            timer.Tick += tick;
            timer.Start();
        }

        void ModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(viewModel.Sessions):
                    (ISet<SessionModel> removedModels, _, ISet<SessionModel> addedModels) = e.Diff<SessionModel>();
                    foreach (SessionModel model in removedModels)
                    {
                        sessions.RemoveSession(model);
                    }
                    foreach (SessionModel model in addedModels)
                    {
                        sessions.AddSession(model);
                    }
                    break;
            }
        }

        void OpenVolumeControlWindow()
        {
            if (!ready)
            {
                return;
            }
            if (vcWindow != null)
            {
                if (!vcWindow.Activate())
                {
                    vcWindow.Close();
                }
                return;
            }
            vcWindow = new VolumeControlWindow(viewModel);
            vcWindow.Closed += delegate { vcWindow = null; };
            ElementHost.EnableModelessKeyboardInterop(vcWindow);
            Rect wa = SystemParameters.WorkArea;
            vcWindow.Left = wa.Right - vcWindow.Width;
            vcWindow.Top = wa.Bottom - vcWindow.Height;
            vcWindow.Show();
            if (!vcWindow.Activate())
            {
                vcWindow.Close();
            }
        }

        void OpenSettingsWindow()
        {
            if (!ready)
            {
                return;
            }
            if (stWindow != null)
            {
                stWindow.Activate();
                return;
            }
            stWindow = new SettingsWindow(viewModel, sessions, wsServer);
            stWindow.Closed += delegate { stWindow = null; };
            ElementHost.EnableModelessKeyboardInterop(stWindow);
            stWindow.Show();
            stWindow.Activate();
        }
    }
}