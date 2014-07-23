Shuffle Link
===============

Shuffle Link is MusicBee plugin to keep multi-part songs playing together in shuffle mode, using the Comments section of each song.

## Installing

* Grab a recent copy of [MusicBee] from the download page (v2.3 should be high enough)
* Copy `mb-ShuffleLink.dll` to the MusicBee plugin directory (usually `C:\Program Files\MusicBee\Plugins`)
* Restart MusicBee if running

## Usage

Put either `Next:`, `Prev:`, or both (separated using new-lines) into the Comments section of a song (right-click, `Edit`, or Shift+Enter), followed by `title=TITLE;artist=ARTIST;album=ALBUM` where `TITLE`, `ARTIST`, and `ALBUM` refer to another song in your library.  The order doesn't matter.  Use `\;` to include a literal `;`.

If no song is found, it's a duplicate, or the reference is otherwise missing or broken, the plugin will stop searching in that direction.

Note: the first and last song in each list must reference the `Next:` and `Prev:` songs, respectively, or the linked playlist might break.

Example:
Let's say you want to link three songs from the Dustforce soundtrack together.  To do so, edit the "Comments" section of each song to something like this:

* Sepia Tone Laboratory

`Next: title=Upside Down Stalagmite;artist=Lifeformed;album=Fastfall`

* Upside Down Stalagmite

`Next: title=Baryogenesis;artist=Lifeformed;album=Fastfall
Prev: title=Sepia Tone Laboratory;artist=Lifeformed;album=Fastfall`

* Baryogenesis

`Prev: title=Upside Down Stalagmite;artist=Lifeformed;album=Fastfall`

## Building

Tools needed:

* Visual Studio 2010 (C# Express edition, or higher)

A standard Visual Studio 'edit code then build' should be fine, nothing special required.

MusicBee API copied directly from the [MusicBee plugins forum post]

[MusicBee]: http://getmusicbee.com/
[MusicBee plugins forum post]: http://getmusicbee.com/forum/index.php?topic=1972.msg9925#msg9925
