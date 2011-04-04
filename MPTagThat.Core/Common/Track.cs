﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using MPTagThat.Core.Common;
using TagLib;
using TagLib.Id3v2;
using Picture = MPTagThat.Core.Common.Picture;

namespace MPTagThat.Core
{
  public class Track
  {
    #region Variables

    private static readonly string[] _standardId3Frames = new string[] { "TPE1", "TPE2", "TALB", "TBPM", "COMM", "TCOM", "TPE3", "TCOP", "TPOS", "TCON", "TIT1", "USLT", "APIC", "POPM", "TIT2", "TRCK", "TYER" };
    private static readonly NLog.Logger log = ServiceScope.Get<ILogger>().GetLogger;

    #endregion

    #region Pubic Methods

    /// <summary>
    /// Read the Tags from the File 
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static TrackData Create(string fileName)
    {
      TrackData track = new TrackData();
      TagLib.File file = null;
      bool error = false;

      try
      {
        TagLib.ByteVector.UseBrokenLatin1Behavior = true;
        file = TagLib.File.Create(fileName);
      }
      catch (CorruptFileException)
      {
        log.Warn("File Read: Ignoring track {0} - Corrupt File!", fileName);
        error = true;
      }
      catch (UnsupportedFormatException)
      {
        log.Warn("File Read: Ignoring track {0} - Unsupported format!", fileName);
        error = true;
      }
      catch (FileNotFoundException)
      {
        log.Warn("File Read: Ignoring track {0} - Physical file no longer existing!", fileName);
        error = true;
      }
      catch (Exception ex)
      {
        log.Error("File Read: Error processing file: {0} {1}", fileName, ex.Message);
        error = true;
      }

      if (error)
      {
        return null;
      }

      TagLib.Id3v2.Tag id3v2tag = null;
      if (file.MimeType.Substring(file.MimeType.IndexOf("/") + 1) == "mp3")
      {
        id3v2tag = file.GetTag(TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
      }

      #region Set Common Values

      track.Id = new Guid();
      track.FullFileName = fileName;
      track.FileName = Path.GetFileName(fileName);
      FileInfo fi = new FileInfo(fileName);
      track.Readonly = fi.IsReadOnly;
      track.TagType = file.MimeType.Substring(file.MimeType.IndexOf("/") + 1);
      #endregion

      #region Set Tags
      // Artist
      track.Artist = String.Join(";", file.Tag.Performers);
      if (track.Artist.Contains("AC;DC"))
      {
        track.Artist = track.Artist.Replace("AC;DC", "AC/DC");
      }

      track.AlbumArtist = String.Join(";", file.Tag.AlbumArtists);
      if (track.AlbumArtist.Contains("AC;DC"))
      {
        track.AlbumArtist = track.AlbumArtist.Replace("AC;DC", "AC/DC");
      }

      track.Album = file.Tag.Album ?? "";
      track.BPM = (int)file.Tag.BeatsPerMinute;
      track.Compilation = id3v2tag == null ? false : id3v2tag.IsCompilation;
      track.Composer = string.Join(";", file.Tag.Composers);
      track.Conductor = file.Tag.Conductor ?? "";
      track.Copyright = file.Tag.Copyright ?? "";

      track.DiscNumber = file.Tag.Disc;
      track.DiscCount = file.Tag.DiscCount;

      track.Genre = string.Join(";", file.Tag.Genres);
      track.Grouping = file.Tag.Grouping ?? "";
      track.Title = file.Tag.Title ?? "";

      track.TrackNumber = file.Tag.Track;
      track.TrackCount = file.Tag.TrackCount;
      track.Year = (int)file.Tag.Year;

      // Pictures
      foreach (IPicture picture in file.Tag.Pictures)
      {
        MPTagThat.Core.Common.Picture pic = new MPTagThat.Core.Common.Picture
        {
          Type = picture.Type,
          MimeType = picture.MimeType,
          Description = picture.Description
        };

        pic.Data = pic.ImageFromData(picture.Data.Data);
        track.Pictures.Add(pic);
      }

      // Comments
      if (track.TagType == "mp3" && id3v2tag != null)
      {
        foreach (CommentsFrame commentsframe in id3v2tag.GetFrames<CommentsFrame>())
        {
          track.ID3Comments.Add(new Comment(commentsframe.Description, commentsframe.Language, commentsframe.Text));
        }
      }
      else
      {
        track.Comment = file.Tag.Comment;
      }

      // Lyrics
      track.Lyrics = file.Tag.Lyrics;
      foreach (UnsynchronisedLyricsFrame lyricsframe in id3v2tag.GetFrames<UnsynchronisedLyricsFrame>())
      {
        track.LyricsFrames.Add(new Lyric(lyricsframe.Description, lyricsframe.Language, lyricsframe.Text));
      }

      // Rating
      track.Rating = 0;
      if (track.TagType == "mp3")
      {
        // First read in all POPM Frames found
        foreach (PopularimeterFrame popmframe in id3v2tag.GetFrames<PopularimeterFrame>())
        {
          track.Ratings.Add(new PopmFrame(popmframe.User, (int)popmframe.Rating, (int)popmframe.PlayCount));
        }

        TagLib.Id3v2.PopularimeterFrame popmFrame = TagLib.Id3v2.PopularimeterFrame.Get(id3v2tag, "MPTagThat", false);
        if (popmFrame != null)
        {
          track.Rating = popmFrame.Rating;
        }
        else
        {
          // Now check for Ape Rating
          TagLib.Ape.Tag apetag = file.GetTag(TagTypes.Ape, true) as TagLib.Ape.Tag;
          TagLib.Ape.Item apeItem = apetag.GetItem("RATING");
          if (apeItem != null)
          {
            string rating = apeItem.ToString();
            try
            {
              track.Rating = Convert.ToInt32(rating);
            }
            catch (Exception)
            { }
          }
        }
      }
      else
      {
        if (track.TagType == "ape")
        {
          TagLib.Ape.Tag apetag = file.GetTag(TagTypes.Ape, true) as TagLib.Ape.Tag;
          TagLib.Ape.Item apeItem = apetag.GetItem("RATING");
          if (apeItem != null)
          {
            string rating = apeItem.ToString();
            try
            {
              track.Rating = Convert.ToInt32(rating);
            }
            catch (Exception)
            { }
          }
        }
      }

      #endregion


      #region Set Properties

      track.DurationTimespan = file.Properties.Duration;

      int fileLength = (int)(fi.Length / 1024);
      track.FileSize = fileLength.ToString();

      track.BitRate = file.Properties.AudioBitrate.ToString();
      track.SampleRate = file.Properties.AudioSampleRate.ToString();
      track.Channels = file.Properties.AudioChannels.ToString();
      track.Version = file.Properties.Description;
      track.CreationTime = string.Format("{0:yyyy-MM-dd HH:mm:ss}", fi.CreationTime);
      track.LastWriteTime = string.Format("{0:yyyy-MM-dd HH:mm:ss}", fi.LastWriteTime);

      #endregion

      // Now copy all Text frames of an ID3 V2

      if (track.TagType == "mp3" && id3v2tag != null)
      {
        foreach (TagLib.Id3v2.Frame frame in id3v2tag.GetFrames())
        {
          string id = frame.FrameId.ToString();
          if (!track.Frames.ContainsKey(id) && !_standardId3Frames.Contains(id))
          {
            track.Frames.Add(id, frame.ToString());
          }
        }

        track.ID3Version = id3v2tag.Version;
      }

      return track;
    }

    /// <summary>
    /// Clear all the tags
    /// </summary>
    /// <param name="track"></param>
    /// <returns></returns>
    public static TrackData ClearTag(TrackData track)
    {
      track.Artist = "";
      track.AlbumArtist = "";
      track.Album = "";
      track.BPM = 0;
      track.ID3Comments.Clear();
      track.Frames.Clear();
      track.Compilation = false;
      track.Composer = "";
      track.Conductor = "";
      track.Copyright = "";

      track.DiscNumber = 0;
      track.DiscCount = 0;

      track.Genre = "";
      track.Grouping = "";
      track.LyricsFrames.Clear();
      track.Pictures.Clear();
      track.Title = "";
      track.TrackNumber = 0;
      track.TrackCount = 0;
      track.Year = 0;
      track.Ratings.Clear();
      return track;
    }

    /// <summary>
    /// Save the Modified file
    /// </summary>
    /// <param name="track"></param>
    /// <returns></returns>
    public static bool SaveFile(TrackData track)
    {
      if (!track.Changed)
      {
        return true;
      }

      TagLib.File file = null;
      bool error = false;
      try
      {
        TagLib.ByteVector.UseBrokenLatin1Behavior = true;
        file = TagLib.File.Create(track.FullFileName);
      }
      catch (CorruptFileException)
      {
        log.Warn("File Read: Ignoring track {0} - Corrupt File!", track.FullFileName);
        error = true;
      }
      catch (UnsupportedFormatException)
      {
        log.Warn("File Read: Ignoring track {0} - Unsupported format!", track.FullFileName);
        error = true;
      }
      catch (FileNotFoundException)
      {
        log.Warn("File Read: Ignoring track {0} - Physical file no longer existing!", track.FullFileName);
        error = true;
      }
      catch (Exception ex)
      {
        log.Error("File Read: Error processing file: {0} {1}", track.FullFileName, ex.Message);
        error = true;
      }

      if (file == null || error)
      {
        log.Error("File Read: Error processing file.: {0}", track.FullFileName);
        return false;
      }

      try
      {
        // Get the ID3 Frame for ID3 specifc frame handling
        TagLib.Id3v1.Tag id3v1tag = null;
        TagLib.Id3v2.Tag id3v2tag = null;
        if (track.TagType.ToLower() == "mp3")
        {
          id3v1tag = file.GetTag(TagTypes.Id3v1, true) as TagLib.Id3v1.Tag;
          id3v2tag = file.GetTag(TagTypes.Id3v2, true) as TagLib.Id3v2.Tag;
        }

        // Remove Tags, if they have been removed in TagEdit Panel
        foreach (TagLib.TagTypes tagType in track.TagsRemoved)
        {
          file.RemoveTags(tagType);
        }

        #region Main Tags
        string[] splitValues = track.Artist.Split(new[] { ';', '|' });
        file.Tag.Performers = splitValues;

        splitValues = track.AlbumArtist.Split(new[] { ';', '|' });
        file.Tag.AlbumArtists = splitValues;

        file.Tag.Album = track.Album.Trim();
        file.Tag.BeatsPerMinute = (uint) track.BPM;
        

        if (track.Comment != "")
        {
          track.Comment = "";
          if (track.TagType.ToLower() == "mp3")
          {
            id3v1tag.Comment = track.Comment;
            foreach (Comment comment in track.ID3Comments)
            {
              CommentsFrame commentsframe = CommentsFrame.Get(id3v2tag, comment.Description, comment.Language, true);
              commentsframe.Text = comment.Text;
            }
          }
          else
            file.Tag.Comment = track.Comment;
        }
        else
        {
          file.Tag.Comment = "";
        }

        if (track.TagType.ToLower() == "mp3")
        {
          id3v2tag.IsCompilation = track.Compilation;
        }

        file.Tag.Disc = track.DiscNumber;
        file.Tag.DiscCount = track.DiscCount;

        splitValues = track.Genre.Split(new[] { ';', '|' });
        file.Tag.Genres = splitValues;

        file.Tag.Title = track.Title;

        file.Tag.Track = track.TrackNumber;
        file.Tag.TrackCount = track.TrackCount;

        file.Tag.Year = (uint)track.Year;

        #endregion

        #region Detailed Information

        splitValues = track.Composer.Split(new[] { ';', '|' });
        file.Tag.Composers = splitValues;
        file.Tag.Conductor = track.Conductor;
        file.Tag.Copyright = track.Copyright;
        file.Tag.Grouping = track.Grouping;

        #endregion

        #region Picture

        List<TagLib.Picture> pics = new List<TagLib.Picture>();
        foreach(Picture pic in track.Pictures)
        {
          ImageConverter imgConverter = new ImageConverter();
          TagLib.Picture tagPic = new TagLib.Picture();

          try
          {
            byte[] byteArray = Picture.ImageToByte(pic.Data);
            ByteVector data = new ByteVector(byteArray);
            tagPic.Data = data;
            tagPic.Description = pic.Description;
            tagPic.MimeType = pic.MimeType;
            tagPic.Type = pic.Type;
            pics.Add(tagPic);
          }
          catch (Exception ex)
          {
            log.Error("Error saving Picture: {0}", ex.Message);
          }

          file.Tag.Pictures = pics.ToArray();
        }

        #endregion

        #region Lyrics

        if (track.Lyrics != "")
        {
          track.Lyrics = "";
          if (track.TagType.ToLower() == "mp3")
          {
            foreach (Lyric lyric in track.LyricsFrames)
            {
              UnsynchronisedLyricsFrame lyricframe = UnsynchronisedLyricsFrame.Get(id3v2tag, lyric.Description, lyric.Language, true);
              lyricframe.Text = lyric.Text;
            }
          }
          else
            file.Tag.Lyrics = track.Lyrics;
        }
        else
        {
          file.Tag.Lyrics = "";
        }
        #endregion

        #region Ratings

        if (track.TagType.ToLower() == "mp3")
        {
          if (track.Ratings.Count > 0)
          {
            foreach (PopmFrame rating in track.Ratings)
            {
              PopularimeterFrame popmFrame = PopularimeterFrame.Get(id3v2tag, rating.User, true);
              popmFrame.Rating = Convert.ToByte(rating.Rating);
              popmFrame.PlayCount = Convert.ToUInt32(rating.PlayCount);
            }
          }
          else
            id3v2tag.RemoveFrames("POPM");
        }
        

        #endregion

        #region Non- Standard Taglib and User Defined Frames

        foreach (string id in track.Frames.Keys)
        {
          ByteVector frameId = new ByteVector(id);
          id3v2tag.SetTextFrame(frameId, track.Frames[id].ToString());
        }

        #endregion
        
        // Now, depending on which frames the user wants to save, we will remove the other Frames
        file = Util.FormatID3Tag(file);
        
        // SAve the file
        file.Save();
      }
      catch (Exception ex)
      {
        log.Error("File Read: Error processing file: {0} {1}", track.FullFileName, ex.Message);
        error = true;
      }

      return error;
    }

    #endregion
  }
}