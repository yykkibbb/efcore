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

    [ConditionalFact]
    public void OriginalValues_ToObject_with_complex_collection_containing_double_nested_nullable_complex_properties()
    {
        var modelBuilder = InMemoryTestHelpers.Instance.CreateConventionBuilder();
        modelBuilder.Entity<JobWithErrorCollection>(eb =>
        {
            eb.ComplexProperty(e => e.Error);
            eb.ComplexCollection(e => e.Errors);
        });
        var model = modelBuilder.FinalizeModel();

        var serviceProvider = InMemoryTestHelpers.Instance.CreateContextServices(model);
        var stateManager = serviceProvider.GetRequiredService<IStateManager>();
        
        // Test with a complex collection where items have double nested nullable complex properties
        var job = new JobWithErrorCollection
        {
            Id = 1,
            Name = "Test Job",
            Errors = new List<JobError>
            {
                // Error with null InnerError (nested complex property is null)
                new JobError
                {
                    Code = "400",
                    Message = "Bad Request",
                    InnerError = null
                },
                // Error with InnerError but its nested InnerError is null
                new JobError
                {
                    Code = "500",
                    Message = "Server Error",
                    InnerError = new JobError
                    {
                        Code = "501",
                        Message = "Not Implemented",
                        InnerError = null
                    }
                },
                // Error with fully populated nested errors
                new JobError
                {
                    Code = "503",
                    Message = "Service Unavailable",
                    InnerError = new JobError
                    {
                        Code = "504",
                        Message = "Gateway Timeout",
                        InnerError = new JobError
                        {
                            Code = "505",
                            Message = "Internal Error"
                        }
                    }
                }
            }
        };
        
        var entityEntry = stateManager.GetOrCreateEntry(job);
        entityEntry.SetEntityState(EntityState.Unchanged);
        
        var entry = new EntityEntry<JobWithErrorCollection>(entityEntry);
        var original = entry.OriginalValues.ToObject() as JobWithErrorCollection;
        
        Assert.NotNull(original);
        Assert.Equal("Test Job", original.Name);
        Assert.NotNull(original.Errors);
        Assert.Equal(3, original.Errors.Count);
        
        // Verify Error1 (null InnerError)
        Assert.Equal("400", original.Errors[0].Code);
        Assert.Equal("Bad Request", original.Errors[0].Message);
        Assert.Null(original.Errors[0].InnerError);
        
        // Verify Error2 (InnerError populated but its InnerError is null)
        Assert.Equal("500", original.Errors[1].Code);
        Assert.Equal("Server Error", original.Errors[1].Message);
        Assert.NotNull(original.Errors[1].InnerError);
        Assert.Equal("501", original.Errors[1].InnerError.Code);
        Assert.Null(original.Errors[1].InnerError.InnerError);
        
        // Verify Error3 (fully populated)
        Assert.Equal("503", original.Errors[2].Code);
        var innerError504 = original.Errors[2].InnerError;
        Assert.NotNull(innerError504);
        Assert.Equal("504", innerError504.Code);
        var innerError505 = innerError504.InnerError;
        Assert.NotNull(innerError505);
        Assert.Equal("505", innerError505.Code);
    }

    private class JobWithErrorCollection
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public JobError Error { get; set; }
        public List<JobError> Errors { get; set; }
    }


    [ConditionalFact]
    public void OriginalValues_ToObject_with_triple_nested_nullable_complex_properties()
    {
        var modelBuilder = InMemoryTestHelpers.Instance.CreateConventionBuilder();
        modelBuilder.Entity<Container>(eb =>
        {
            eb.ComplexProperty(e => e.Level1, lb =>
            {
                lb.IsRequired(false);
                lb.ComplexProperty(l1 => l1.Level2, l2b =>
                {
                    l2b.IsRequired(false);
                });
            });
        });
        var model = modelBuilder.FinalizeModel();

        var serviceProvider = InMemoryTestHelpers.Instance.CreateContextServices(model);
        var stateManager = serviceProvider.GetRequiredService<IStateManager>();
        
        // Test with triple nested nullable complex properties at various levels
        var entity1 = new Container
        {
            Id = 1,
            Name = "Container with null Level1",
            Level1 = null
        };
        var entry1 = stateManager.GetOrCreateEntry(entity1);
        entry1.SetEntityState(EntityState.Unchanged);
        
        var entity2 = new Container
        {
            Id = 2,
            Name = "Container with Level1 but null Level2",
            Level1 = new Level1Data
            {
                Info = "Level 1 info",
                Level2 = null
            }
        };
        var entry2 = stateManager.GetOrCreateEntry(entity2);
        entry2.SetEntityState(EntityState.Unchanged);
        
        var entity3 = new Container
        {
            Id = 3,
            Name = "Container with Level2 but null Level3",
            Level1 = new Level1Data
            {
                Info = "Level 1 info",
                Level2 = new Level2Data
                {
                    Description = "Level 2 desc",
                    Level3 = null
                }
            }
        };
        var entry3 = stateManager.GetOrCreateEntry(entity3);
        entry3.SetEntityState(EntityState.Unchanged);
        
        var entity4 = new Container
        {
            Id = 4,
            Name = "Container fully populated",
            Level1 = new Level1Data
            {
                Info = "Level 1 info",
                Level2 = new Level2Data
                {
                    Description = "Level 2 desc",
                    Level3 = new Level3Data
                    {
                        Detail = "Level 3 detail"
                    }
                }
            }
        };
        var entry4 = stateManager.GetOrCreateEntry(entity4);
        entry4.SetEntityState(EntityState.Unchanged);
        
        // Test entity1 (null Level1)
        var original1 = new EntityEntry<Container>(entry1).OriginalValues.ToObject() as Container;
        Assert.NotNull(original1);
        Assert.Equal("Container with null Level1", original1.Name);
        Assert.Null(original1.Level1);
        
        // Test entity2 (Level1 populated, Level2 null)
        var original2 = new EntityEntry<Container>(entry2).OriginalValues.ToObject() as Container;
        Assert.NotNull(original2);
        Assert.Equal("Container with Level1 but null Level2", original2.Name);
        Assert.NotNull(original2.Level1);
        Assert.Equal("Level 1 info", original2.Level1.Info);
        Assert.Null(original2.Level1.Level2);
        
        // Test entity3 (Level2 populated, Level3 null)
        var original3 = new EntityEntry<Container>(entry3).OriginalValues.ToObject() as Container;
        Assert.NotNull(original3);
        Assert.Equal("Container with Level2 but null Level3", original3.Name);
        Assert.NotNull(original3.Level1);
        Assert.NotNull(original3.Level1.Level2);
        Assert.Equal("Level 2 desc", original3.Level1.Level2.Description);
        Assert.False(original3.Level1.Level2.Level3.HasValue);
        
        // Test entity4 (fully populated)
        var original4 = new EntityEntry<Container>(entry4).OriginalValues.ToObject() as Container;
        Assert.NotNull(original4);
        Assert.Equal("Container fully populated", original4.Name);
        Assert.NotNull(original4.Level1);
        Assert.NotNull(original4.Level1.Level2);
        Assert.True(original4.Level1.Level2.Level3.HasValue);
        Assert.Equal("Level 3 detail", original4.Level1.Level2.Level3.Value.Detail);
    }

    private class Container
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Level1Data Level1 { get; set; }
    }

    private class Level1Data
    {
        public string Info { get; set; }
        public Level2Data Level2 { get; set; }
    }

    private class Level2Data
    {
        public string Description { get; set; }
        public Level3Data? Level3 { get; set; }
    }

    private struct Level3Data
    {
        public string Detail { get; set; }
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
