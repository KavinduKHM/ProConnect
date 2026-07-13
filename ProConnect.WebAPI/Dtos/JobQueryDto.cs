namespace ProConnect.WebAPI.Dtos
{
    /// <summary>Filter/sort/page options for GET /api/jobs, bound from the query string.</summary>
    public class JobQueryDto
    {
        private const int MaxPageSize = 50;

        private int _pageSize = 12;
        private int _page = 1;

        /// <summary>Free-text match against title, description and location.</summary>
        public string? Search { get; set; }

        /// <summary>
        /// When true, Search is matched by meaning rather than keyword, so "water damage"
        /// finds "burst pipe under the sink".
        /// </summary>
        public bool Semantic { get; set; }

        public int? CategoryId { get; set; }

        /// <summary>Defaults to "Open" when omitted; pass "All" to drop the status filter.</summary>
        public string? Status { get; set; }

        public bool? IsUrgent { get; set; }

        public decimal? MinBudget { get; set; }
        public decimal? MaxBudget { get; set; }

        public string? Location { get; set; }

        /// <summary>newest | oldest | budgetHigh | budgetLow | mostBids</summary>
        public string? SortBy { get; set; }

        public int Page
        {
            get => _page;
            set => _page = value < 1 ? 1 : value;
        }

        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value is < 1 or > MaxPageSize ? MaxPageSize : value;
        }
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => PageSize == 0 ? 0 : (int)System.Math.Ceiling(TotalCount / (double)PageSize);
    }
}
