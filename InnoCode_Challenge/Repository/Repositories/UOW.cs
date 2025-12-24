using DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Repository.IRepositories;
using Microsoft.EntityFrameworkCore;             
using Microsoft.EntityFrameworkCore.Storage;

namespace Repository.Repositories
{
    public class UOW : IUOW
    {
        private bool disposed = false;
        private readonly ContestDbContext _dbContext;
        private IDbContextTransaction? _transaction;
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
                    _transaction?.Dispose();
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
            if (!_dbContext.Database.IsRelational())
                return;

            _transaction ??= _dbContext.Database.BeginTransaction();
        }

        public void CommitTransaction()
        {
            if (_transaction is null)
                return;

            _transaction.Commit();
            _transaction.Dispose();
            _transaction = null;
        }

        public void RollBack()
        {
            if (_transaction is null)
                return;

            _transaction.Rollback();
            _transaction.Dispose();
            _transaction = null;
        }

    }
}
