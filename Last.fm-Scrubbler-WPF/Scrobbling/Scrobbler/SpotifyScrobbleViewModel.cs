﻿using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using Scrubbler.Helper;
using Scrubbler.Properties;
using SpotifyAPI.Local;
using SpotifyAPI.Local.Models;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace Scrubbler.Scrobbling.Scrobbler
{
  /// <summary>
  /// ViewModel for the Spotify <see cref="MediaPlayerScrobbleControl"/>
  /// </summary>
  public class SpotifyScrobbleViewModel : MediaPlayerScrobbleViewModelBase
  {
    #region Properties

    /// <summary>
    /// When true, tries to connect to Spotify on startup.
    /// </summary>
    public override bool AutoConnect
    {
      get { return Settings.Default.SpotifyAutoConnect; }
      set
      {
        Settings.Default.SpotifyAutoConnect = value;
        NotifyOfPropertyChange();
      }
    }

    /// <summary>
    /// The name of the current playing track.
    /// </summary>
    public override string CurrentTrackName => _currentResponse?.Track?.TrackResource?.Name;

    /// <summary>
    /// The name of the current artist.
    /// </summary>
    public override string CurrentArtistName => _currentResponse?.Track?.ArtistResource?.Name;

    /// <summary>
    /// The name of the current album.
    /// </summary>
    public override string CurrentAlbumName => _currentResponse?.Track?.AlbumResource?.Name;

    /// <summary>
    /// The length of the current track.
    /// </summary>
    public override int CurrentTrackLength => _currentResponse?.Track == null ? 0 : _currentResponse.Track.Length;

    #endregion Properties

    #region Member

    /// <summary>
    /// Connection to the local Spotify client.
    /// </summary>
    private SpotifyLocalAPI _spotify;

    /// <summary>
    /// Info about the current Spotify status.
    /// </summary>
    private StatusResponse _currentResponse;

    /// <summary>
    /// Timer counting the played seconds.
    /// </summary>
    private Timer _counterTimer;

    /// <summary>
    /// Timer updating the <see cref="_currentResponse"/>.
    /// </summary>
    private Timer _refreshTimer;

    /// <summary>
    /// Uri of the last played track.
    /// </summary>
    private string _lastTrack;

    /// <summary>
    /// Lock object to lock the data update.
    /// </summary>
    private readonly object _lockAnchor = new object();

    #endregion Member

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="windowManager">WindowManager used to display dialogs.</param>
    /// <param name="trackAPI">Last.fm API object for getting track information.</param>
    /// <param name="albumAPI">Last.fm API object for getting album information.</param>
    /// <param name="artistAPI">Last.fm API object for getting artist information.</param>
    public SpotifyScrobbleViewModel(IExtendedWindowManager windowManager, ITrackApi trackAPI, IAlbumApi albumAPI, IArtistApi artistAPI)
      : base(windowManager, "Spotify Scrobbler", trackAPI, albumAPI, artistAPI)
    {
      PercentageToScrobble = 0.5;
      _counterTimer = new Timer(1000);
      _counterTimer.Elapsed += _counterTimer_Elapsed;
      _refreshTimer = new Timer(1000);
      _refreshTimer.Elapsed += _refreshTimer_Elapsed;

      if (AutoConnect)
        Connect();
    }

    /// <summary>
    /// Connects to the Spotify client.
    /// </summary>
    public override void Connect()
    {
      try
      {
        EnableControls = false;

        if (IsConnected)
          Disconnect();

        _spotify = new SpotifyLocalAPI();
        SpotifyLocalAPI.RunSpotify();
        SpotifyLocalAPI.RunSpotifyWebHelper();

        if (!SpotifyLocalAPI.IsSpotifyRunning())
        {
          OnStatusUpdated("Error connecting to Spotify: Client not running");
          return;
        }
        if (!SpotifyLocalAPI.IsSpotifyWebHelperRunning())
        {
          OnStatusUpdated("Error connecting to Spotify: WebHelper not running");
          return;
        }

        if (!_spotify.Connect())
        {
          OnStatusUpdated("Error connecting to Spotify: Unknown error");
        }
        else
        {
          ConnectEvents();
          _currentResponse = _spotify.GetStatus();
          UpdateCurrentTrackInfo();

          _refreshTimer.Start();

          if (_currentResponse.Playing)
            _counterTimer.Start();
          else
            _counterTimer.Stop();

          IsConnected = true;
          OnStatusUpdated("Successfully connected to Spotify");
        }
      }
      catch (Exception ex)
      {
        OnStatusUpdated(string.Format("Fatal error connecting to Spotify: {0}", ex.Message));
      }
      finally
      {
        EnableControls = true;
      }
    }

    /// <summary>
    /// Disconnects from the Spotify client.
    /// </summary>
    public override void Disconnect()
    {
      _counterTimer.Stop();
      _refreshTimer.Stop();
      DisconnectEvents();
      _spotify.Dispose();
      _currentResponse = null;
      _lastTrack = null;
      CountedSeconds = 0;
      IsConnected = false;
      UpdateCurrentTrackInfo();
    }

    /// <summary>
    /// Connects the necessary Spotify events.
    /// </summary>
    private void ConnectEvents()
    {
      _spotify.ListenForEvents = true;
      _spotify.OnPlayStateChange += _spotify_OnPlayStateChange;
    }

    /// <summary>
    /// Disconnects the Spotify events.
    /// </summary>
    private void DisconnectEvents()
    {
      _spotify.ListenForEvents = false;
      _spotify.OnPlayStateChange -= _spotify_OnPlayStateChange;
    }

    /// <summary>
    /// Starts or stops the <see cref="_counterTimer"/> depending
    /// on the Spotify play state.
    /// </summary>
    /// <param name="sender">Ignored.</param>
    /// <param name="e">EventArgs containing the current play state.</param>
    private void _spotify_OnPlayStateChange(object sender, PlayStateEventArgs e)
    {
      if (e.Playing)
        _counterTimer.Start();
      else
        _counterTimer.Stop();
    }

    /// <summary>
    /// Counts the listened seconds and scrobbles when
    /// the user listened long enough.
    /// </summary>
    /// <param name="sender">Ignored.</param>
    /// <param name="e">Ignored.</param>
    private void _counterTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
      if (++CountedSeconds == CurrentTrackLengthToScrobble)
      {
        _counterTimer.Stop();
        Scrobble().Forget();
      }
    }

    /// <summary>
    /// Updates the Spotify info.
    /// Disconnects if we can't a track.
    /// </summary>
    /// <param name="sender">Ignored.</param>
    /// <param name="e">Ignored.</param>
    private void _refreshTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
      lock (_lockAnchor)
      {
        _lastTrack = _currentResponse?.Track?.TrackResource.Uri;
        _currentResponse = _spotify.GetStatus();

        if (_lastTrack != _currentResponse?.Track?.TrackResource?.Uri)
        {
          CountedSeconds = 0;

          if (_currentResponse?.Playing ?? false)
            _counterTimer.Start();

          UpdateCurrentTrackInfo();
        }
      }
    }

    /// <summary>
    /// Scrobbles the currently playing track.
    /// </summary>
    /// <returns>Task.</returns>
    public override async Task Scrobble()
    {
      if (CanScrobble && !_currentResponse.Track.IsAd())
      {
        EnableControls = false;

        Scrobble s = null;
        try
        {
          OnStatusUpdated(string.Format("Trying to scrobble '{0}'...", CurrentTrackName));
          // lock while acquiring current data
          lock (_lockAnchor)
          {
            s = new Scrobble(CurrentArtistName, CurrentAlbumName, CurrentTrackName, DateTime.Now)
            {
              Duration = TimeSpan.FromSeconds(CurrentTrackLength),
            };
          }

          var response = await Scrobbler.ScrobbleAsync(s, true);
          if (response.Success && response.Status == LastResponseStatus.Successful)
            OnStatusUpdated(string.Format("Successfully scrobbled '{0}'", s.Track));
          else if(response.Status == LastResponseStatus.Cached)
            OnStatusUpdated(string.Format("Scrobbling '{0}' failed. Scrobble has been cached", s.Track));
          else
            OnStatusUpdated(string.Format("Error while scrobbling '{0}': {1}", s.Track, response.Status));
        }
        catch (Exception ex)
        {
          OnStatusUpdated(string.Format("Fatal error while trying to scrobble '{0}: {1}", s.Track, ex.Message));
        }
        finally
        {
          EnableControls = true;
        }
      }
    }
  }
}