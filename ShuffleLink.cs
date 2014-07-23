/*
 * ShuffleLink.cs
 *
 * Author:
 *      Shane Synan <digitalcircuit36939@gmail.com>
 *
 * Copyright (c) 2014 
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */


using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

/* Known issues
 * When pressing Previous repeatedly to back out of a set of linked songs (phantom queue), it will go back to the song MusicBee first shuffled to,
 *  requiring an extra press of Prev.  E.g.:
 *  4th -> 3rd -> 2nd -> 1st -> song MusicBee shuffled to, e.g. 2nd -> prior songs
 *  Additionally, the shuffle history will be reset completely
 * 
 * When moving out of a set of linked songs (phantom queue), sometimes it will not clean up the linked songs right away.  It should be cleaned up
 *  in one or two song changes.
 * 
 */

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        // Standard MusicBee API code
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();

        // Shuffle Link specific variables
        private System.Timers.Timer timerUserModifiedPlaylist = new System.Timers.Timer (250);
        //  Originally 500 ms, but that could break when skipping through tracks
        private bool hasUserModifiedPlaylist = false;
        // HACK:  MusicBee does not appear to have a way to distinguish between TrackChanged due to end of song,
        //  and changing due to a manual selection.  This distinguishes a change in the Now Playing List to prevent track-changed
        //  events from re-linking the songs together.
        // If 1 <-> 2 <-> 3
        //  Starting song 2 directly would not cause a forced switch back to song 1.
        private bool isProgramModifyingPlaylist = false;
        // HACK: Continuing above, prevent the time-out from being called if the program itself changes the Now Playing List.
        //  This prevents bugs in the playlist clean-up code.

        // Song information as defined by a string (e.g. comment field)
        // E.g. title=This;artist=Somethings;album=totally\; yeah
        private struct BasicSongIdentifier
        {
            public string Title;
            public string Artist;
            public string Album;
        }

        private const char Song_ParamSep = ';';
        private const char Song_ParamAssign = '=';

        // Keep track of what playlist was last auto-created.  Only if this changes does a new playlist need
        //  inserted into the NowPlayingList.
        private List<string> LastContinuousPlaylist = new List<string>();

        // Whenever the last continuous playlist is finished, remove the added tracks.
        private int LastPlaylist_StartingIndex = -1;

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
                    if (isProgramModifyingPlaylist == true)
                    {
                        isProgramModifyingPlaylist = false;
                        break;
                    }
                    // Ignore this time-out if self-modified
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

                    List<string> continuousPlaylist = buildContinuousPlaylist(sourceFileUrl);

                    // If cleanup needed, do it, otherwise, check next things
                    //  Cleanup needed if:  Linked generated playlists not the same
                    //                      Index did not increment as expected (shuffled back into existing generated playlist)
                    if ((LastPlaylist_StartingIndex != -1) && (checkIfPlaylistsEqual(LastContinuousPlaylist, continuousPlaylist) == false))
                    {
                        isProgramModifyingPlaylist = true;
                        // Remove all automatically-added songs, to keep the playlist nice and tidy.
                        for (int i = 0; i < LastContinuousPlaylist.Count; i++)
                        {
                            mbApiInterface.NowPlayingList_RemoveAt(LastPlaylist_StartingIndex + 1);
                        }

                        // Reset everything for next round
                        cleanupLinkedPlaylist();
                    }

                    // If there's only one song or less, something either broke, or no playlist needs strung together.
                    //  Otherwise, load 'em up!
                    if (continuousPlaylist.Count > 1)
                    {
                        if (checkIfPlaylistsEqual(LastContinuousPlaylist, continuousPlaylist))
                        {
                            // This playlist was already set up in the Now Playing List.  Don't do anything.
                        }
                        else
                        {
                            // First time encountering a song from this list.  Start from scratch.
                            //  Also keep a record of this playlist
                            LastContinuousPlaylist.Clear();
                            // Tell the time-out timer to not fire
                            isProgramModifyingPlaylist = true;
                            foreach (string track in continuousPlaylist)
                            {
                                mbApiInterface.NowPlayingList_QueueNext(track);
                                LastContinuousPlaylist.Add(track);
                            }

                            // Keep note of which indexes were used when adding songs, to remove them later
                            LastPlaylist_StartingIndex = mbApiInterface.NowPlayingList_GetCurrentIndex();

                            // Switch to the song
                            //  It's simpler to always do this, rather than add logic for if it's on the first song
                            mbApiInterface.Player_PlayNextTrack();
                        }
                    }

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

        /// <summary>
        /// Resets all variables for an ongoing linked song playlist
        /// </summary>
        private void cleanupLinkedPlaylist()
        {
            LastPlaylist_StartingIndex = -1;

            // Clean up the last playlist, too, in case it's needed again
            LastContinuousPlaylist.Clear();
        }

        /// <summary>
        /// Checks if the two list of strings are equal in length and items
        /// </summary>
        /// <param name="Playlist1">First list of strings</param>
        /// <param name="Playlist2">Second list of strings</param>
        /// <returns>True if equal, false if not</returns>
        private bool checkIfPlaylistsEqual(List<string> Playlist1, List<string> Playlist2)
        {
            if (Playlist1.Count != Playlist2.Count)
                return false;

            for (int i = 0; i < Playlist1.Count; i++)
            {
                if (Playlist1[i] != Playlist2[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Given a URL to a song, builds a playlist of linked songs via the comment field
        /// </summary>
        /// <param name="CurrentSongURL">The current song, or starting point of the search</param>
        /// <returns>A list of song URLs, minimum 1 (current song), or more if links found</returns>
        private List<string> buildContinuousPlaylist(string CurrentSongURL)
        {
            List<string> temporaryPlaylist = new List<string>();

            temporaryPlaylist.Add(CurrentSongURL);
            // Add the assigned song, so it's first in the list (for now)
            string initialSongComment = mbApiInterface.Library_GetFileTag(CurrentSongURL, MetaDataType.Comment);

            string curResult = "";
            string curSongComment = initialSongComment;

            // Check each song, recursively, until end is found
            do
            {
                curResult = getSongURLFromCommentHeader("Next", curSongComment);
                if(temporaryPlaylist.Contains (curResult))
                    break;
                // Don't allow duplicates

                if (curResult != null && curResult.Trim () != "")
                {
                    curSongComment = mbApiInterface.Library_GetFileTag(curResult, MetaDataType.Comment);
                    // If the result isn't null (song found), try to find the next one

                    // Add to the end of the list
                    temporaryPlaylist.Add(curResult);
                }
            } while (curResult != null);

            curSongComment = initialSongComment;
            // Check each song, recursively (from the first provided one), until end is found
            do
            {
                curResult = getSongURLFromCommentHeader("Prev", curSongComment);
                if (temporaryPlaylist.Contains(curResult))
                    break;
                // Don't allow duplicates

                if (curResult != null && curResult.Trim () != "")
                {
                    curSongComment = mbApiInterface.Library_GetFileTag(curResult, MetaDataType.Comment);
                    // If the result isn't null (song found), try to find the next one

                    // Add to the beginning of the list
                    temporaryPlaylist.Insert(0, curResult);
                }
            } while (curResult != null);

            // All's done, return the results
            //  If no matches, it will be a playlist of length 1
            return temporaryPlaylist;
        }

        /// <summary>
        /// Checks the passed song comment for a reference to another track
        /// </summary>
        /// <param name="CommentHeader">The start of the identifier, e.g. Next: or Prev:</param>
        /// <param name="SongComment">The comment of the song</param>
        /// <returns>URL to a song, or null if not found or error encountered</returns>
        private string getSongURLFromCommentHeader(string CommentHeader, string SongComment)
        {
            if (SongComment == null || SongComment.Contains(CommentHeader + ":") == false)
                return null;
            // If null or lacking a "Header:" line, nothing there
            List<string> fields = new List<string>();

            // If there's multiple lines, add 'em all, otherwise just add the lone line
            if (SongComment.Contains("\n") == true)
            {
                fields.AddRange(SongComment.Split('\n'));
            }
            else
            {
                fields.Add(SongComment);
            }

            foreach (string field in fields)
            {
                if (field.StartsWith(CommentHeader + ":") == true)
                {
                    try
                    {
                        // Starts with proper header:
                        //  Try to grab the song info from the part after header, e.g.
                        //  Next: title=Title;artist=Artist;album=Album
                        BasicSongIdentifier songInformation = parseToSong (field.Substring (field.IndexOf (":") + 1).Trim ());
                        return getSongURI (songInformation.Artist, songInformation.Album, songInformation.Title, true, true);
                    }
                    catch (ArgumentException)
                    {
                        // Realistically, there's no need to bring the whole music player down if this fails
                        //  Try with the other fields
                        continue;
                    }
                }
            }
            //  Assume as if no song linked
            return null;
        }

        /// <summary>
        /// Takes in a song identifier as a string, returns a structure with the fields filled out
        /// </summary>
        /// <param name="SongIdentifier">String of fields representing the song, e.g. title=This;artist=Somethings;album=totally\; yeah</param>
        /// <returns>BasicSongIdentifier structure, which contains title, artist, and album</returns>
        private BasicSongIdentifier parseToSong(string SongIdentifier)
        {
            if (SongIdentifier.Contains(Song_ParamAssign.ToString()) == false)
                throw new System.ArgumentException(String.Format ("Invalid song identifier, no '{1}' character in it.  Example: 'title{1}A{0}artist{1}B{0}album{1}C'", Song_ParamSep, Song_ParamAssign), "SongIdentifier");
            // Must have at least one specified identifier to be valid

            BasicSongIdentifier referencedSong = new BasicSongIdentifier ();

            // \; is an escaped semicolon, let it pass through without being split
            string[] fields = SongIdentifier.Trim().Replace("\\" + Song_ParamSep, "IDENTIFIER_FOR_ESCAPED_CHAR").Split(Song_ParamSep);
            string[] fieldEntries;
            foreach (string field in fields)
            {
                if (field.Contains(Song_ParamAssign.ToString()) == false)
                    continue;
                // Ignore empty fields

                // Swap the ';' back in
                string modifiedField = field.Replace("IDENTIFIER_FOR_ESCAPED_CHAR", Song_ParamSep.ToString ());
                fieldEntries = modifiedField.Split(Song_ParamAssign);
                // Check the part in front, title=, artist=, etc
                // Once found, grab everything after the first '=' sign, and add a character to account for the '='
                //  Allows for more = signs later in the field
                switch (fieldEntries[0])
                {
                    case "title":
                        referencedSong.Title = modifiedField.Substring(modifiedField.IndexOf(Song_ParamAssign) + 1);
                        break;
                    case "artist":
                        referencedSong.Artist = modifiedField.Substring(modifiedField.IndexOf(Song_ParamAssign) + 1);
                        break;
                    case "album":
                        referencedSong.Album = modifiedField.Substring(modifiedField.IndexOf(Song_ParamAssign) + 1);
                        break;
                    default:
                        // Might be a new type of tag.  Ignore it for now.
                        break;
                }
            }
            return referencedSong;
        }

        /// <summary>
        /// Gets a URL to a song in the music library
        /// </summary>
        /// <param name="Artist">Artist of song</param>
        /// <param name="Album">Album of song</param>
        /// <param name="Title">Title of song</param>
        /// <param name="mustMatchAll">If all conditions must be met, or just one</param>
        /// <param name="isExactMatch">If tags must strictly match, or just contain the text</param>
        /// <returns>URL to first song found, or null if no match</returns>
        private string getSongURI(string Artist, string Album, string Title, bool mustMatchAll, bool isExactMatch)
        {
            string[] tracks = {};
            mbApiInterface.Library_QueryFilesEx(generateSearchQuery(Artist, Album, Title, mustMatchAll, isExactMatch), ref tracks);
            if (tracks.Length > 0)
            {
                return tracks[0];
                // At some point in the future, there might be a need for dealing with multiple tracks.
                //  For now, just assume the first one.
            }
            else
            {
                return null;
                // No song found
            }
        }

        /// <summary>
        /// Generates an XML-based SmartPlaylist with given inputs
        /// </summary>
        /// <param name="Artist">Artist of song</param>
        /// <param name="Album">Album of song</param>
        /// <param name="Title">Title of song</param>
        /// <param name="mustMatchAll">If all conditions must be met, or just one</param>
        /// <param name="isExactMatch">If tags must strictly match, or just contain the text</param>
        /// <returns>XML-based SmartPlaylist for MusicBee queries</returns>
        private string generateSearchQuery(string Artist, string Album, string Title, bool mustMatchAll, bool isExactMatch)
        {
            string matchMode = (mustMatchAll ? "All" : "Any");
            string searchMode = (isExactMatch ? "Is" : "Contains");
            return string.Format("<SmartPlaylist>\n" +
                                    "<Source Type=\"1\">\n" +
                                        "<Conditions CombineMethod=\"{0}\"> \n" +
                                            "<Condition Field=\"Artist\" Comparison=\"{1}\" Value=\"{2}\" />\n" +
                                            "<Condition Field=\"Album\" Comparison=\"{1}\" Value=\"{3}\" />\n" +
                                            "<Condition Field=\"Title\" Comparison=\"{1}\" Value=\"{4}\" />\n" +
                                        "</Conditions>\n" +
                                    "</Source>\n" +
                                 "</SmartPlaylist>", matchMode, searchMode, Artist, Album, Title);
            //                                       {0}        {1}         {2}     {3}    {4}
        }

   }
}