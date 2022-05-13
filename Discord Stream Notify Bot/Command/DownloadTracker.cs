#nullable disable
using Discord;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command;

//https://gitlab.com/Kwoth/nadekobot/-/blob/v4/src/NadekoBot/Common/DownloadTracker.cs
public class DownloadTracker : ICommandService
{
    private ConcurrentDictionary<ulong, DateTime> LastDownloads { get; } = new();
    private readonly SemaphoreSlim _downloadUsersSemaphore = new(1, 1);

    /// <summary>
    ///     Ensures all users on the specified guild were downloaded within the last hour.
    /// </summary>
    /// <param name="guild">Guild to check and potentially download users from</param>
    /// <returns>Task representing download state</returns>
    public async Task EnsureUsersDownloadedAsync(IGuild guild)
    {
#if GLOBAL_NADEKO
        return;
#endif
        await _downloadUsersSemaphore.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;

            // download once per hour at most
            var added = LastDownloads.AddOrUpdate(guild.Id,
                now,
                (_, old) => now - old > TimeSpan.FromHours(1) ? now : old);

            // means that this entry was just added - download the users
            if (added == now)
                await guild.DownloadUsersAsync();
        }
        finally
        {
            _downloadUsersSemaphore.Release();
        }
    }
}