using static StorageMaster.Tests.TestKit;

namespace StorageMaster.Tests
{
    public static class LocksTests
    {
        [Test]
        private static void ToggleLocksAndUnlocksAndIsSaved()
        {
            Lock(2, 3);
            Assert.True(Locks.IsLocked(2, 3), "Slot locked");
            Assert.True(!Locks.IsLocked(3, 2), "Other slot not locked");

            // Read back from the character's saved data, as after logging in again.
            Locks._loadedFor = null;
            Assert.True(Locks.IsLocked(2, 3), "Slot still locked after reloading");

            Lock(2, 3);
            Assert.True(!Locks.IsLocked(2, 3), "Slot unlocked");
            Locks._loadedFor = null;
            Assert.True(!Locks.IsLocked(2, 3), "Slot still unlocked after reloading");
        }
    }
}
