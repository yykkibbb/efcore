// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.EntityFrameworkCore.ChangeTracking.Internal;

public class PropertyValuesTest
{
    [ConditionalFact]
    public void Can_safely_get_originalvalue_and_currentvalue_with_tryget()
    {
        // Arrange
        const string NameValue = "Simple Name";
        const string NewNameValue = "A New Name";

        using var ctx = new CurrentValuesDb();
        var entity = ctx.SimpleEntities.Add(new SimpleEntity { Name = NameValue });
        ctx.SaveChanges();

        entity.Entity.Name = NewNameValue;

        // Act
        var current = entity.CurrentValues.TryGetValue<string>("Name", out var currentName);
        var original = entity.OriginalValues.TryGetValue<string>("Name", out var originalName);

        // Assert
        Assert.True(current);
        Assert.True(original);

        Assert.Equal(NameValue, originalName);
        Assert.Equal(NewNameValue, currentName);
    }

    [ConditionalFact]
    public void OriginalValues_ToObject_with_nested_nullable_complex_property()
    {
        using var ctx = new NestedComplexDb();
        
        // Test case 1: Entity with no complex property  
        var job1 = new Job { Id = 1, Name = "Job with No Error" };
        ctx.Jobs.Add(job1);
        ctx.SaveChanges();
        
        var original1 = ctx.Entry(job1).OriginalValues.ToObject() as Job;
        Assert.NotNull(original1);
        Assert.Equal("Job with No Error", original1.Name);
        Assert.Null(original1.Error);
        
        // Test case 2: Entity with nested complex properties all populated
        var job2 = new Job
        {
            Id = 2,
            Name = "Job with Error + Inner Error",
            Error = new JobError
            {
                Code = "500",
                Message = "Internal Server Error",
                InnerError = new JobError
                {
                    Code = "501",
                    Message = "Not Implemented"
                }
            }
        };
        ctx.Jobs.Add(job2);
        ctx.SaveChanges();
        
        var original2 = ctx.Entry(job2).OriginalValues.ToObject() as Job;
        Assert.NotNull(original2);
        Assert.Equal("Job with Error + Inner Error", original2.Name);
        Assert.NotNull(original2.Error);
        Assert.Equal("500", original2.Error.Code);
        Assert.NotNull(original2.Error.InnerError);
        Assert.Equal("501", original2.Error.InnerError.Code);
        
        // Test case 3: Entity with null nested complex property (InnerError is null) - this was throwing InvalidCastException
        var job3 = new Job
        {
            Id = 3,
            Name = "Job with Error only",
            Error = new JobError
            {
                Code = "400",
                Message = "Bad Request"
            }
        };
        ctx.Jobs.Add(job3);
        ctx.SaveChanges();
        
        var original3 = ctx.Entry(job3).OriginalValues.ToObject() as Job;
        Assert.NotNull(original3);
        Assert.Equal("Job with Error only", original3.Name);
        Assert.NotNull(original3.Error);
        Assert.Equal("400", original3.Error.Code);
        Assert.Null(original3.Error.InnerError); // This was causing InvalidCastException before the fix
    }

    [ConditionalFact(Skip = "Complex collection with nested nullable complex properties not yet fully supported. Issue #31411")]
    public void OriginalValues_ToObject_with_complex_collection_containing_double_nested_nullable_complex_properties()
    {
        var modelBuilder = InMemoryTestHelpers.Instance.CreateConventionBuilder();
        modelBuilder.Entity<BlogWithTags>(eb =>
        {
            eb.ComplexCollection(e => e.Tags);
        });
        var model = modelBuilder.FinalizeModel();

        var serviceProvider = InMemoryTestHelpers.Instance.CreateContextServices(model);
        var stateManager = serviceProvider.GetRequiredService<IStateManager>();
        
        // Test with a complex collection where items have double nested nullable complex properties
        var blog = new BlogWithTags
        {
            Id = 1,
            Title = "Test Blog",
            Tags = new List<TagWithMetadata>
            {
                // Tag with null Metadata (complex property is null)
                new TagWithMetadata
                {
                    Name = "Tag1",
                    Metadata = null
                },
                // Tag with Metadata but null nested InnerData (nested complex property is null)
                new TagWithMetadata
                {
                    Name = "Tag2",
                    Metadata = new TagMetadata
                    {
                        Author = "AuthorA",
                        CreatedDate = "2024-01-01",
                        InnerData = null
                    }
                },
                // Tag with fully populated nested complex properties
                new TagWithMetadata
                {
                    Name = "Tag3",
                    Metadata = new TagMetadata
                    {
                        Author = "AuthorB",
                        CreatedDate = "2024-01-02",
                        InnerData = new InnerMetadata
                        {
                            Source = "SourceX"
                        }
                    }
                }
            }
        };
        
        var entityEntry = stateManager.GetOrCreateEntry(blog);
        entityEntry.SetEntityState(EntityState.Unchanged);
        
        var entry = new EntityEntry<BlogWithTags>(entityEntry);
        var original = entry.OriginalValues.ToObject() as BlogWithTags;
        
        Assert.NotNull(original);
        Assert.Equal("Test Blog", original.Title);
        Assert.NotNull(original.Tags);
        Assert.Equal(3, original.Tags.Count);
        
        // Verify Tag1 (null Metadata)
        Assert.Equal("Tag1", original.Tags[0].Name);
        Assert.Null(original.Tags[0].Metadata);
        
        // Verify Tag2 (Metadata populated but InnerData is null)
        Assert.Equal("Tag2", original.Tags[1].Name);
        Assert.NotNull(original.Tags[1].Metadata);
        Assert.Equal("AuthorA", original.Tags[1].Metadata.Author);
        Assert.Null(original.Tags[1].Metadata.InnerData);
        
        // Verify Tag3 (fully populated)
        Assert.Equal("Tag3", original.Tags[2].Name);
        Assert.NotNull(original.Tags[2].Metadata);
        Assert.Equal("AuthorB", original.Tags[2].Metadata.Author);
        Assert.NotNull(original.Tags[2].Metadata.InnerData);
        Assert.Equal("SourceX", original.Tags[2].Metadata.InnerData.Source);
    }

    private class BlogWithTags
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public List<TagWithMetadata> Tags { get; set; }
    }

    private class TagWithMetadata
    {
        public string Name { get; set; }
        public TagMetadata Metadata { get; set; }
    }

    private class TagMetadata
    {
        public string Author { get; set; }
        public string CreatedDate { get; set; }
        public InnerMetadata InnerData { get; set; }
    }

    private class InnerMetadata
    {
        public string Source { get; set; }
    }

    private class NestedComplexDb : DbContext
    {
        public DbSet<Job> Jobs { get; set; }

        protected internal override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseInMemoryDatabase("NestedComplexDb");

        protected internal override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Job>(b =>
            {
                b.ComplexProperty(x => x.Error);
            });
        }
    }

    private class Job
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public JobError Error { get; set; }
    }

    private class JobError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public JobError InnerError { get; set; }
    }

    [ConditionalFact]
    public void Should_not_throw_error_when_property_do_not_exist()
    {
        // Arrange
        const string NameValue = "Simple Name";
        const string NewNameValue = "A New Name";

        using var ctx = new CurrentValuesDb();
        var entity = ctx.SimpleEntities.Add(new SimpleEntity { Name = NameValue });
        ctx.SaveChanges();

        entity.Entity.Name = NewNameValue;

        // Act
        var current = entity.CurrentValues.TryGetValue<string>("Non_Existent_Property", out var non_existent_current);
        var original = entity.OriginalValues.TryGetValue<string>("Non_Existent_Property", out var non_existent_original);

        // Assert
        Assert.False(current);
        Assert.False(original);

        Assert.Null(non_existent_current);
        Assert.Null(non_existent_original);
    }

    private class CurrentValuesDb : DbContext
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public DbSet<SimpleEntity> SimpleEntities { get; set; }

        protected internal override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseInMemoryDatabase("DB1");
    }

    private class SimpleEntity
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public IEnumerable<RelatedEntity> RelatedEntities { get; set; }
    }

    private class RelatedEntity
    {
        public int Id { get; set; }

        public int? SimpleEntityId { get; set; }

        public SimpleEntity SimpleEntity { get; set; }

        public string Name { get; set; }
    }
}
