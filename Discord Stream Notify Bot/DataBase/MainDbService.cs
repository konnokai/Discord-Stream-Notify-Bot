namespace Discord_Stream_Notify_Bot.DataBase
{
    public class MainDbService
    {
        private readonly string _connectionString;

        public MainDbService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public MainDbContext GetDbContext()
        {
            return new MainDbContext(_connectionString);
        }
    }
}
