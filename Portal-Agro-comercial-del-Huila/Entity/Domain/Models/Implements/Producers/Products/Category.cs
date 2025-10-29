﻿using Entity.Domain.Models.Base;

namespace Entity.Domain.Models.Implements.Producers.Products
{
    public class Category : BaseModel
    {
        public string Name { get; set; }
        public int? ParentCategoryId { get; set; }
        public Category? ParentCategory { get; set; }

        public ICollection<Category> SubCategories { get; set; } = [];
        public ICollection<Product> Products { get; set; } = [];


    }
}
