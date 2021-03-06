﻿using EtherSound.Settings;
using EtherSound.ViewModel;
using EtherSound.WebSocket;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EtherSound.View
{
    /// <summary>
    /// Logique d'interaction pour AdvancedWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        readonly RootModel model;
        readonly SessionManager sessions;
        readonly Server wsListener;

        internal SettingsWindow(RootModel model, SessionManager sessions, Server wsListener)
        {
            this.model = model;
            this.sessions = sessions;
            this.wsListener = wsListener;
            InitializeComponent();
            DataContext = model;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Return)
            {
                Close();
            }
            base.OnPreviewKeyDown(e);
        }

        async void EditSession(SessionSettings settings, bool isNew)
        {
            await sessions.RefreshDevices().ConfigureAwait(true);
            SessionSettingsModel model = new SessionSettingsModel(this.model, settings, sessions.Devices, isNew);
            model.SettingsUpdated += delegate
            {
                if (model.IsNew)
                {
                    SessionModel newSession = Program.CreateSessionModel(settings);
                    this.model.AddSession(newSession);
                    this.model.SelectedSession = newSession;
                    model.IsNew = false;
                }
                else
                {
                    sessions.RestartPending();
                }
            };
            SessionSettingsWindow ssWindow = new SessionSettingsWindow(model)
            {
                Owner = this
            };
            ssWindow.ShowDialog();
        }

        private void AddSession_Click(object sender, RoutedEventArgs e)
        {
            EditSession(SessionSettings.CreateNew(), true);
        }

        private void MoveSessionUp_Click(object sender, RoutedEventArgs e)
        {
            model.SetSessionPosition(model.SelectedSession, -1, true);
        }

        private void MoveSessionDown_Click(object sender, RoutedEventArgs e)
        {
            model.SetSessionPosition(model.SelectedSession, 1, true);
        }

        private void RemoveSession_Click(object sender, RoutedEventArgs e)
        {
            SessionModel session = model.SelectedSession;
            if (MessageBox.Show(this, string.Format("Voulez-vous vraiment supprimer la ligne {0} ?{1}{1}Cette opération est irréversible !", session.SourceName, Environment.NewLine), Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }
            model.RemoveSession(session);
        }

        private void ChangeSessionColor_Click(object sender, RoutedEventArgs e)
        {
            Color? color = ColorDialog.Show(this, model.SelectedSession.Color);
            if (color.HasValue)
            {
                model.SelectedSession.Color = color.GetValueOrDefault();
            }
        }

        private void ChangeSessionSource_Click(object sender, RoutedEventArgs e)
        {
            EditSession(model.SelectedSession.Settings, false);
        }

        private void NetworkSettings_Click(object sender, RoutedEventArgs e)
        {
            NetworkSettingsModel model = new NetworkSettingsModel(this.model);
            model.SettingsUpdated += delegate
            {
                if (model.NetworkSinkDefaultsChanged)
                {
                    model.NetworkSinkDefaultsChanged = false;
                    foreach (SessionModel session in this.model.Sessions)
                    {
                        if (null != session.Settings.NetworkSink)
                        {
                            session.Settings.RestartPending = true;
                        }
                    }
                    sessions.RestartPending();
                }
                if (model.WebSocketEndpointChanged)
                {
                    model.WebSocketEndpointChanged = false;
                    wsListener.Settings = this.model.Settings.WebSocketEndpoint;
                }
            };
            NetworkSettingsWindow nsWindow = new NetworkSettingsWindow(model)
            {
                Owner = this
            };
            nsWindow.ShowDialog();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
