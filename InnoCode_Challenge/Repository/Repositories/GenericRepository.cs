using System.Linq.Expressions;
using DataAccess.Entities;
using Repository.IRepositories;
using Utility.PaginatedList;
using Microsoft.EntityFrameworkCore;

namespace Repository.Repositories
{

    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly ContestDbContext _context;
        protected readonly DbSet<T> _dbSet;

        public GenericRepository(ContestDbContext dbContext)
        {
            _context = dbContext;
            _dbSet = _context.Set<T>();
        }

        public IQueryable<T> Entities => _context.Set<T>();

        public void Delete(object entity)
        {
            _dbSet.Remove((T)entity);
        }

        public async Task DeleteAsync(object entity)
        {
            _dbSet.Remove((T)entity);
            await Task.CompletedTask;
        }

        public T? Find(Expression<Func<T, bool>> predicate)
        {
            return _dbSet.FirstOrDefault(predicate);
        }

        public T? GetById(object id)
        {

            return _dbSet.Find(id);
        }

        public async Task<T?> GetByIdAsync(object id)
        {
            return await _dbSet.FindAsync(id);
        }

        public void Insert(T obj)
        {
            _dbSet.Add(obj);
        }

        public async Task InsertAsync(T obj)
        {
            await _dbSet.AddAsync(obj);
        }
        public void InsertRange(List<T> obj)
        {
            _dbSet.AddRange(obj);
        }



        public void Save()
        {
            _context.SaveChanges();
        }

        public async Task SaveAsync()
        {
            await _context.SaveChangesAsync();
        }

        public void Update(T obj)
        {
            _context.Entry(obj).State = EntityState.Modified;
        }

        public Task UpdateAsync(T obj)
        {
            return Task.FromResult(_dbSet.Update(obj));
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        public async Task InsertRangeAsync(List<T> obj)
        {
            await _dbSet.AddRangeAsync(obj);
        }

        public async Task<PaginatedList<T>> GetPagingAsync(IQueryable<T> query, int index, int pageSize)
        {
            return await query.GetPaginatedList(index, pageSize);
        }
    }

}
