using System.Collections.Generic;

namespace MapTalkie.Utils
{
    public class ListResponse<T>
    {
        public ListResponse(List<T> items)
        {
            Items = items;
        }

        public List<T> Items { get; set; }
    }
}