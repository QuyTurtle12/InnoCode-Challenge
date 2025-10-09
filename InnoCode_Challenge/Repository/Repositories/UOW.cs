using DataAccess.Entities;
using Repository.IRepositories;
using Repository.Repositories;

namespace Repository.Repositories
{
    public class UOW : IUOW
    {
        private bool disposed = false;
        private readonly ContestDbContext _dbContext;
        public UOW(ContestDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public IGenericRepository<T> GetRepository<T>() where T : class
        {
            return new GenericRepository<T>(_dbContext);
        }
        public async Task SaveAsync()
        {
            await _dbContext.SaveChangesAsync();
        }
        public void Save()
        {
            _dbContext.SaveChanges();
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    _dbContext.Dispose();
                }
            }
            disposed = true;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        public void BeginTransaction()
        {
            _dbContext.Database.BeginTransaction();
        }

        public void CommitTransaction()
        {
            _dbContext.Database.CommitTransaction();
        }

        public void RollBack()
        {
            _dbContext.Database.RollbackTransaction();
        }

    }
}
