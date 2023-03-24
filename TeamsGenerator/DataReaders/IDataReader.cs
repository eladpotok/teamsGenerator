using System.Collections.Generic;

namespace TeamsGenerator.DataReaders
{
    internal interface IDataReader<T>
    {
        T Read();
    }
}
