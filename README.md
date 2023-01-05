# EF Core 

**Prerequisites:**  
.NET 6.0  
SQL Server. You may download it here: https://www.microsoft.com/en-us/sql-server/sql-server-downloads (any of the free editions are fine).

**How to run:**  
`dotnet restore`  
`dotnet build`  
`dotnet test`  

**Code explanation and the problem that is being reproduced:**   
In `Program.cs` you will find one entity `Product` that has a `ConcurrencyCheck` on `Price` property and one context `ProductContext`. Besides the initial setup and cleanup code, there are two tests for the method `ApplyDiscountToCoconut`. This method creates an `SqlServerRetryingExecutionStrategy` and executes a function inside it. The function creates a transaction (since there are several DB actions that are being invoked), gets one existing Product from the DB, **modifies its Price** and saves it with `acceptAllChangesOnSuccess` flag set to `true`. Then a transient error is being emulated in order to trigger a retry and transaction never commits.  
The two tests each provide a different way in which the Price modification happens. The first one uses an assignment subtraction operator which results in `ApplyDiscountToCoconut` throwing a `DbUpdateConcurrencyException`. The second one uses an assignment operator which results in the same method throwing `RetryLimitExceededException`.  
I cannot tell if it is an expected behavior or not, but it does feel weird.   
It seems the `ChangeTracker` didn't `AcceptAllChanges` in the **assignment operator test** and the transaction was retried until the retry limit was reached.   
Meanwhile in the **assignment subtraction operator test** the code threw a `DbUpdateConcurrencyException` on the first retry as it should since the `ChangeTracker` accepted all changes on `SaveChangesAsync(true)` before the first retry. Then, on the first retry the modified entity was treated as an `original values` which resulted in a failed concurrency check because the `database values` ([relevant docs on `original` and `database` values](https://learn.microsoft.com/en-us/ef/core/saving/concurrency?tabs=data-annotations#resolving-concurrency-conflicts)) are different (the transaction never committed on the initial run).  
