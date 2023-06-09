using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.DataAccess;
using Entities.Concrete;
using Entities.Dtos;

namespace DataAccess.Repositories.CustomerRepository
{
    public interface ICustomerDal : IEntityRepository<Customer>
    {
        Task<List<CustomerListDto>> GetListDto();
        Task<CustomerListDto> GetDto(int id);
        Task<List<OperationClaim>> GetCustomerOperatinonClaims(int customerIdId);
    }
}
