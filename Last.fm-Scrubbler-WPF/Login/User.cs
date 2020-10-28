﻿using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Scrubbler.Login
{
  /// <summary>
  /// Represents a last.fm user.
  /// </summary>
  [DataContract]
  public class User
  {
    #region Properties

    /// <summary>
    /// Event that fires when the <see cref="RecentScrobbles"/>
    /// change.
    /// </summary>
    public event EventHandler RecentScrobblesChanged;

    /// <summary>
    /// Allowed scrobbles per day.
    /// </summary>
    public const int MAXSCROBBLESPERDAY = 3000;

    /// <summary>
    /// Username of this user.
    /// </summary>
    [DataMember]
    public string Username { get; private set; }

    /// <summary>
    /// Login token of this user.
    /// </summary>
    [DataMember]
    public string Token { get; private set; }

    /// <summary>
    /// If this user is a subscriber.
    /// </summary>
    [DataMember]
    public bool IsSubscriber { get; private set; }

    /// <summary>
    /// Cached recent scrobbles.
    /// Call <see cref="UpdateRecentScrobbles"/> to update.
    /// </summary>
    public IEnumerable<LastTrack> RecentScrobblesCache { get; private set; }

    #endregion Properties

    #region Member

    internal IUserApi _userAPI;

    #endregion Member

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="username">Username of the user.</param>
    /// <param name="token">Login token.</param>
    /// <param name="isSubscriber">If this user is a subscriber.</param>
    public User(string username, string token, bool isSubscriber, IUserApi userApi)
    {
      Username = username;
      Token = token;
      IsSubscriber = isSubscriber;
      _userAPI = userApi ?? throw new ArgumentNullException(nameof(userApi));
    }

    public async Task UpdateRecentScrobbles()
    {
      var scrobbles = new List<Scrobble>(3000);
      // get the last 3000 tracks
      var page1 = await _userAPI.GetRecentScrobbles(Username, DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(24)), null, false, 1, 1000);
      var page2 = await _userAPI.GetRecentScrobbles(Username, DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(24)), null, false, 2, 1000);
      var page3 = await _userAPI.GetRecentScrobbles(Username, DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(24)), null, false, 3, 1000);

      RecentScrobblesCache = page1.Content.Concat(page2.Content).Concat(page3.Content).ToArray();
    }
  }
}