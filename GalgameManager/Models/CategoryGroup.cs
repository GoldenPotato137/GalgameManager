﻿using GalgameManager.Enums;

namespace GalgameManager.Models;

public class CategoryGroup
{
    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public List<Category> Categories = new();
    public CategoryGroupType Type;

    public CategoryGroup()
    {
    }

    public CategoryGroup(string name, CategoryGroupType type)
    {
        Type = type;
        Name = name;
    }

    public CategoryGroup(string name)
    {
        Name = name;
    }

    public override string ToString() => Name;
}