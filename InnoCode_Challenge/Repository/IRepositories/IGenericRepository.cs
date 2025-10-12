using System.Linq.Expressions;
using Utility.PaginatedList;

namespace Repository.IRepositories
{
    public interface IGenericRepository<T> where T : class
    {
        //queryable
        IQueryable<T> Entities { get; }

        //void
        T? GetById(object id);
        void Insert(T obj);
        void InsertRange(List<T> obj);
        Task InsertRangeAsync(List<T> obj);

        void Update(T obj);
        void Delete(object entity);
        void Save();

        //Task
        Task<T?> GetByIdAsync(object id);
        Task<IEnumerable<T>> GetAllAsync();
        Task InsertAsync(T obj);
        Task UpdateAsync(T obj);
        Task DeleteAsync(object entity);
        Task SaveAsync();
        Task<PaginatedList<T>> GetPagingAsync(IQueryable<T> query, int index, int pageSize);

        //another
        T? Find(Expression<Func<T, bool>> predicate);


    }



}
