using Microsoft.EntityFrameworkCore;

namespace Discord_Stream_Notify_Bot.DataBase
{
    public class VideoContext : DbContext
    {
        public DbSet<Table.Video> Video { get; set; }

        public bool UpdateAndSave(Table.Video video)
        {
            try
            {
                Video.Update(video);
                SaveChanges();
                Dispose();
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error(ex.ToString());
                return false;
            }
        }
    }
}
