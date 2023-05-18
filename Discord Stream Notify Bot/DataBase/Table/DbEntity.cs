using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class DbEntity
    {
        [Key]
        public int Id { get; set; }
        public DateTime? DateAdded { get; set; } = DateTime.Now;
    }
}
