using Xunit;

// WPF keeps ONE application-wide resource assembly and ONE dispatcher, and the tests below open real
// windows on the one desktop the machine has. When two test classes run side by side, one window
// takes the keyboard focus away from the other, and a test that depends on where the focus happens
// to be turns into a coin toss.
//
// xUnit runs test COLLECTIONS in parallel by default, and every test class is its own collection, so
// the whole project was running thirty-odd windows at once. The suite is serialised instead: one
// desktop's focus belongs to one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
