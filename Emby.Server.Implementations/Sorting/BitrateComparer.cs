using System;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Sorting;

namespace Emby.Server.Implementations.Sorting
{
    /// <summary>
    /// Comparer for sorting by total bitrate.
    /// </summary>
    public class BitrateComparer : IBaseItemComparer
    {
        /// <inheritdoc />
        public ItemSortBy Type => ItemSortBy.VideoBitRate;

        /// <inheritdoc />
        public int Compare(BaseItem? x, BaseItem? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            return (x.TotalBitrate ?? 0).CompareTo(y.TotalBitrate ?? 0);
        }
    }
}
