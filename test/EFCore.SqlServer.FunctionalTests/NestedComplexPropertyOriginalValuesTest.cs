// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.EntityFrameworkCore;

public class NestedComplexPropertyOriginalValuesTest : IClassFixture<NestedComplexPropertyOriginalValuesTest.NestedComplexPropertyOriginalValuesFixture>
{
    private readonly NestedComplexPropertyOriginalValuesFixture _fixture;

    public NestedComplexPropertyOriginalValuesTest(NestedComplexPropertyOriginalValuesFixture fixture)
    {
        _fixture = fixture;
    }

    [ConditionalFact]
    public async Task OriginalValues_ToObject_with_null_nested_complex_property()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        // Add three test cases from the bug report
        context.Jobs.Add(new Job { Id = Guid.NewGuid(), Name = "Job with No Error" });
        
        context.Jobs.Add(new Job
        {
            Id = Guid.NewGuid(),
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
        });

        context.Jobs.Add(new Job
        {
            Id = Guid.NewGuid(),
            Name = "Job with Error only",
            Error = new JobError
            {
                Code = "400",
                Message = "Bad Request"
            }
        });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Retrieve and try to call ToObject() on OriginalValues
        var jobs = await context.Jobs.ToListAsync();
        foreach (var job in jobs)
        {
            var original = context.Entry(job).OriginalValues.ToObject() as Job;
            Assert.NotNull(original);
            Assert.Equal(job.Id, original.Id);
            Assert.Equal(job.Name, original.Name);
            
            if (job.Error == null)
            {
                Assert.Null(original.Error);
            }
            else
            {
                Assert.NotNull(original.Error);
                Assert.Equal(job.Error.Code, original.Error.Code);
                Assert.Equal(job.Error.Message, original.Error.Message);
                
                if (job.Error.InnerError == null)
                {
                    Assert.Null(original.Error.InnerError);
                }
                else
                {
                    Assert.NotNull(original.Error.InnerError);
                    Assert.Equal(job.Error.InnerError.Code, original.Error.InnerError.Code);
                    Assert.Equal(job.Error.InnerError.Message, original.Error.InnerError.Message);
                }
            }
        }
    }

    public class Job
    {
        public Guid Id { get; set; }
        public required string Name { get; set; }
        public JobError? Error { get; set; }
    }

    public class JobError
    {
        public required string Code { get; set; }
        public required string Message { get; set; }
        public JobError? InnerError { get; set; }
    }

    public class JobContext : DbContext
    {
        public JobContext(DbContextOptions<JobContext> options)
            : base(options)
        {
        }

        public DbSet<Job> Jobs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Job>(b =>
            {
                b.ComplexProperty(x => x.Error, x => x.ToJson());
            });
        }
    }

    public class NestedComplexPropertyOriginalValuesFixture : SharedStoreFixtureBase<JobContext>
    {
        protected override string StoreName
            => nameof(NestedComplexPropertyOriginalValuesTest);

        protected override ITestStoreFactory TestStoreFactory
            => SqlServerTestStoreFactory.Instance;
    }
}
