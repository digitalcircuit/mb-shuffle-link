using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        // Standard MusicBee API code
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();

        // Shuffle Link specific variables
        private System.Timers.Timer timerUserModifiedPlaylist = new System.Timers.Timer (500);
        private bool hasUserModifiedPlaylist = false;
        // HACK:  MusicBee does not appear to have a way to distinguish between TrackChanged due to end of song,
        //  and changing due to a manual selection.  This distinguishes a change in the NPL from preventing track-changed
        //  events from re-linking the songs together.
        // If 1 <-> 2 <-> 3
        //  Starting song 2 directly would not cause a forced switch back to song 1.

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "Shuffle Link";
            about.Description = "When shuffling music, keeps multi-part songs playing together";
            about.Author = "Shane Synan";
            about.TargetApplication = "";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            about.Type = PluginType.General;
            about.VersionMajor = 0;  // your plugin version
            about.VersionMinor = 1;
            about.Revision = 0;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            about.ConfigurationPanelHeight = 0;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            // panelHandle will only be set if you set about.ConfigurationPanelHeight to a non-zero value
            // keep in mind the panel width is scaled according to the font the user has selected
            // if about.ConfigurationPanelHeight is set to 0, you can display your own popup window
            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Panel.FromHandle(panelHandle);
                Label prompt = new Label();
                prompt.AutoSize = true;
                prompt.Location = new Point(0, 0);
                prompt.Text = "prompt:";
                TextBox textBox = new TextBox();
                textBox.Bounds = new Rectangle(60, 0, 100, textBox.Height);
                configPanel.Controls.AddRange(new Control[] { prompt, textBox });
            }
            return false;
        }
       
        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        // receive event notifications from MusicBee
        // you need to set about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // perform some action depending on the notification type
            switch (type)
            {
                case NotificationType.PluginStartup:
                    // perform startup initialisation
                    timerUserModifiedPlaylist.Elapsed += new System.Timers.ElapsedEventHandler(timerUserModifiedPlaylist_Elapsed);
                    break;
                case NotificationType.NowPlayingListChanged:
                    hasUserModifiedPlaylist = true;
                    timerUserModifiedPlaylist.Start();
                    // Assume the user modified the N.P.L.; prevent automatic song linking from overriding a chosen song
                    break;
                case NotificationType.TrackChanged:
                    if (mbApiInterface.Player_GetShuffle () != true)
                        break;
                    // No need to do anything if shuffle is not enabled

                    if (hasUserModifiedPlaylist == true)
                        break;
                    // Don't override automatic song linking


                    string song_comments = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Comment);

                    // TODO: Remove this part, just testing out the MusicBee API
                    //  Does NowPlayingList_QueueNext use the Play Queue?
                    //  Testing confirms:  Yes, it does.
                    mbApiInterface.Library_QueryFiles("artist=Lifeformed");
                    // TODO: How to handle searching for more than one thing at a time?
                    string file_url = mbApiInterface.Library_QueryGetNextFile();
                    mbApiInterface.NowPlayingList_QueueNext(file_url);

                    // ...
                    break;
            }
        }

        private void  timerUserModifiedPlaylist_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Reasonable amount of time has passed; any TrackChanged events that happen now should result from
            //  MusicBee actually reaching the end of a song
            timerUserModifiedPlaylist.Stop();
            hasUserModifiedPlaylist = false;
        }

   }
}