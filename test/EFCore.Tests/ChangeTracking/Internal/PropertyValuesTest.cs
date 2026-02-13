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
#nullable enable
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
#nullable disable
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

#nullable enable
    private class Job
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public JobError? Error { get; set; }
    }

    private class JobError
    {
        public string Code { get; set; } = null!;
        public string Message { get; set; } = null!;
        public JobError? InnerError { get; set; }
    }
#nullable disable

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
