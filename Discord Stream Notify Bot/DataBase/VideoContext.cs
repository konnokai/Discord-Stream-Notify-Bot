using Microsoft.EntityFrameworkCore;

namespace Discord_Stream_Notify_Bot.DataBase
{
    public class VideoContext : DbContext
    {
        public DbSet<Table.Video> Video { get; set; }

        public bool UpdateAndSave(Table.Video video)
        {
            Video.Update(video);
            var saveTime = DateTime.Now;
            bool saveFailed;

            do
            {
                saveFailed = false;
                try
                {
                    SaveChanges();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    saveFailed = true;
                    foreach (var item in ex.Entries)
                    {
                        try
                        {
                            item.Reload();
                        }
                        catch (Exception ex2)
                        {
                            Log.Error($"VideoContext-SaveChanges-Reload");
                            Log.Error(item.DebugView.ToString());
                            Log.Error(ex2.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"VideoContext-SaveChanges: {ex}");
                    Log.Error(ChangeTracker.DebugView.LongView);
                }
            } while (saveFailed && DateTime.Now.Subtract(saveTime) <= TimeSpan.FromMinutes(1));

            Dispose();

            return DateTime.Now.Subtract(saveTime) >= TimeSpan.FromMinutes(1);
        }
    }
}
