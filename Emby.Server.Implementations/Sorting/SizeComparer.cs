using System;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Sorting;

namespace Emby.Server.Implementations.Sorting
{
    /// <summary>
    /// Comparer for sorting by file size.
    /// </summary>
    public class SizeComparer : IBaseItemComparer
    {
        /// <inheritdoc />
        public ItemSortBy Type => ItemSortBy.Size;

        /// <inheritdoc />
        public int Compare(BaseItem? x, BaseItem? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            return (x.Size ?? 0).CompareTo(y.Size ?? 0);
        }
    }
}
