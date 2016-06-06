// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using live.asp.net.Models;
using Microsoft.ApplicationInsights;
using Microsoft.AspNet.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.OptionsModel;
using Microsoft.Extensions.WebEncoders;

namespace live.asp.net.Services
{
    public class YouTubeShowsService : IShowsService
    {
        public const string CacheKey = nameof(YouTubeShowsService);

        private readonly IHostingEnvironment _env;
        private readonly AppSettings _appSettings;
        private readonly IMemoryCache _cache;
        private readonly TelemetryClient _telemetry;

        public YouTubeShowsService(
            //Dependency Injection
            IHostingEnvironment env,
            IOptions<AppSettings> appSettings,
            //Cached die shows, sodass man nicht ständig zu Youtube muss.
            IMemoryCache memoryCache,
            //das braucht man innerhalb von Azure z.B. um zu wissen wie oft man Youtube aufruft.
            TelemetryClient telemetry)
        {
            _env = env;
            _appSettings = appSettings.Value;
            _cache = memoryCache;
            _telemetry = telemetry;
        }

        /// <summary>
        /// Hauptmethode, die die Hauptarbeit verrichtet
        /// </summary>
        /// <param name="user"></param>
        /// <param name="disableCache"></param>
        /// <returns></returns>
        public async Task<ShowList> GetRecordedShowsAsync(ClaimsPrincipal user, bool disableCache)
        {
            //Habe ich den YouTubeApiKey konfiguriert in meinen Appsettings? Wenn nicht, macht es keinen Sinn eine List mit Youtubeshows zu erstellen
            if (string.IsNullOrEmpty(_appSettings.YouTubeApiKey))
            {
                return new ShowList { Shows = DesignData.Shows };
            }

            //Hier sind nur die 3 Microsoft Gurus authendicated
            if (user.Identity.IsAuthenticated && disableCache)
            {
                //Cache umgehen, bzw. direkt Youtube aufrufen
                return await GetShowsList();
            }

            var result = _cache.Get<ShowList>(CacheKey);

            //noch nichts im Cache drin..
            if (result == null)
            {
                result = await GetShowsList();

                _cache.Set(CacheKey, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1)
                });
            }

            return result;
        }

        /// <summary>
        /// The meat. Hier wird Youtube aufgerufen.
        /// </summary>
        /// <returns></returns>
        private async Task<ShowList> GetShowsList()
        {
            //YoutubeService ist eine .NET Youtube API um mit Youtube arbeiten zu können
            using (var client = new YouTubeService(new BaseClientService.Initializer
            {
                ApplicationName = _appSettings.YouTubeApplicationName,
                
                //_appSettings.YouTubeApiKey kommt von den Secrets oder der Environment
                //Populated vom Configurationsystem und das widerum kann von der Environment oder den Appsettings konfiguriert sein.
                ApiKey = _appSettings.YouTubeApiKey
            }))
            {
                var listRequest = client.PlaylistItems.List("snippet");
                listRequest.PlaylistId = _appSettings.YouTubePlaylistId;
                listRequest.MaxResults = 3 * 8;

                var requestStart = DateTimeOffset.UtcNow;
                var playlistItems = await listRequest.ExecuteAsync();
                _telemetry.TrackDependency("YouTube.PlayListItemsApi", "List", requestStart, DateTimeOffset.UtcNow - requestStart, true);

                var result = new ShowList();

                result.Shows = playlistItems.Items.Select(item => new Show
                {
                    Provider = "YouTube",
                    ProviderId = item.Snippet.ResourceId.VideoId,
                    Title = GetUsefulBitsFromTitle(item.Snippet.Title),
                    Description = item.Snippet.Description,
                    ShowDate = DateTimeOffset.Parse(item.Snippet.PublishedAtRaw, null, DateTimeStyles.RoundtripKind),
                    ThumbnailUrl = item.Snippet.Thumbnails.High.Url,
                    Url = GetVideoUrl(item.Snippet.ResourceId.VideoId, item.Snippet.PlaylistId, item.Snippet.Position ?? 0)
                }).ToList();

                if (!string.IsNullOrEmpty(playlistItems.NextPageToken))
                {
                    result.MoreShowsUrl = GetPlaylistUrl(_appSettings.YouTubePlaylistId);
                }

                return result;
            }
        }

        private static string GetUsefulBitsFromTitle(string title)
        {
            if (title.Count(c => c == '-') < 2)
            {
                return string.Empty;
            }

            var lastHyphen = title.LastIndexOf('-');
            if (lastHyphen >= 0)
            {
                var result = title.Substring(lastHyphen + 1);
                return result;
            }

            return string.Empty;
        }

        private static string GetVideoUrl(string id, string playlistId, long itemIndex)
        {
            var encodedId = UrlEncoder.Default.UrlEncode(id);
            var encodedPlaylistId = UrlEncoder.Default.UrlEncode(playlistId);
            var encodedItemIndex = UrlEncoder.Default.UrlEncode(itemIndex.ToString());

            return $"https://www.youtube.com/watch?v={encodedId}&list={encodedPlaylistId}&index={encodedItemIndex}";
        }

        private static string GetPlaylistUrl(string playlistId)
        {
            var encodedPlaylistId = UrlEncoder.Default.UrlEncode(playlistId);

            return $"https://www.youtube.com/playlist?list={encodedPlaylistId}";
        }

        private static class DesignData
        {
            private static readonly TimeSpan _pdtOffset = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time").BaseUtcOffset;

            public static readonly string LiveShow = null;

            public static readonly IList<Show> Shows = new List<Show>
            {
                new Show
                {
                    ShowDate = new DateTimeOffset(2015, 7, 21, 9, 30, 0, _pdtOffset),
                    Title = "ASP.NET Community Standup - July 21st 2015",
                    Provider = "YouTube",
                    ProviderId = "7O81CAjmOXk",
                    ThumbnailUrl = "http://img.youtube.com/vi/7O81CAjmOXk/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=7O81CAjmOXk&index=1&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },
                new Show
                {
                    ShowDate = new DateTimeOffset(2015, 7, 14, 15, 30, 0, _pdtOffset),
                    Title = "ASP.NET Community Standup - July 14th 2015",
                    Provider = "YouTube",
                    ProviderId = "bFXseBPGAyQ",
                    ThumbnailUrl = "http://img.youtube.com/vi/bFXseBPGAyQ/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=bFXseBPGAyQ&index=2&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },

                new Show
                {
                    ShowDate = new DateTimeOffset(2015, 7, 7, 15, 30, 0, _pdtOffset),
                    Title = "ASP.NET Community Standup - July 7th 2015",
                    Provider = "YouTube",
                    ProviderId = "APagQ1CIVGA",
                    ThumbnailUrl = "http://img.youtube.com/vi/APagQ1CIVGA/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=APagQ1CIVGA&index=3&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },
                new Show
                {
                    ShowDate = DateTimeOffset.Now.AddDays(-28),
                    Title = "ASP.NET Community Standup - July 21st 2015",
                    Provider = "YouTube",
                    ProviderId = "7O81CAjmOXk",
                    ThumbnailUrl = "http://img.youtube.com/vi/7O81CAjmOXk/mqdefault.jpg",
                    Url = "https://www.youtube.com/watch?v=7O81CAjmOXk&index=1&list=PL0M0zPgJ3HSftTAAHttA3JQU4vOjXFquF"
                },
            };
        }
    }
}
