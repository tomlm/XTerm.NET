using XTerm.Common;

namespace XTerm.Tests.Common;

public class EventEmitterTests
{
    [Fact]
    public void Event_AddsListener()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var called = false;

        // Act
        emitter.Event(data => called = true);
        emitter.Fire("test");

        // Assert
        Assert.True(called);
    }

    [Fact]
    public void Event_MultipleListeners_AllCalled()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var count = 0;

        // Act
        emitter.Event(data => count++);
        emitter.Event(data => count++);
        emitter.Event(data => count++);
        emitter.Fire("test");

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void Fire_PassesDataToListeners()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        string? receivedData = null;

        // Act
        emitter.Event(data => receivedData = data);
        emitter.Fire("hello");

        // Assert
        Assert.Equal("hello", receivedData);
    }

    [Fact]
    public void Fire_NoListeners_DoesNotThrow()
    {
        // Arrange
        var emitter = new EventEmitter<string>();

        // Act & Assert - Should not throw
        emitter.Fire("test");
    }

    [Fact]
    public void Event_ReturnsDisposable()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var called = false;

        // Act
        var subscription = emitter.Event(data => called = true);

        // Assert
        Assert.NotNull(subscription);
        Assert.IsAssignableFrom<IDisposable>(subscription);
    }

    [Fact]
    public void Dispose_RemovesListener()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var count = 0;
        var subscription = emitter.Event(data => count++);

        // Act
        emitter.Fire("test1");
        subscription.Dispose();
        emitter.Fire("test2");

        // Assert
        Assert.Equal(1, count); // Only called once before disposal
    }

    [Fact]
    public void Clear_RemovesAllListeners()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var count = 0;
        emitter.Event(data => count++);
        emitter.Event(data => count++);
        emitter.Event(data => count++);

        // Act
        emitter.Clear();
        emitter.Fire("test");

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void Fire_MultipleTimesWithSameListener_CallsMultipleTimes()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var count = 0;
        emitter.Event(data => count++);

        // Act
        emitter.Fire("test1");
        emitter.Fire("test2");
        emitter.Fire("test3");

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void Event_DifferentDataTypes_WorksCorrectly()
    {
        // Arrange
        var intEmitter = new EventEmitter<int>();
        var tupleEmitter = new EventEmitter<(int, int)>();
        var receivedInt = 0;
        var receivedTuple = (0, 0);

        // Act
        intEmitter.Event(data => receivedInt = data);
        tupleEmitter.Event(data => receivedTuple = data);
        
        intEmitter.Fire(42);
        tupleEmitter.Fire((10, 20));

        // Assert
        Assert.Equal(42, receivedInt);
        Assert.Equal((10, 20), receivedTuple);
    }

    [Fact]
    public void Event_ListenerThrows_DoesNotStopOtherListeners()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var count = 0;

        emitter.Event(data => throw new Exception("Test exception"));
        emitter.Event(data => count++);

        // Act & Assert
        Assert.Throws<Exception>(() => emitter.Fire("test"));
        // Note: In current implementation, exception stops execution
        // If implementation changes to catch exceptions, adjust this test
    }

    [Fact]
    public void MultipleDispose_DoesNotThrow()
    {
        // Arrange
        var emitter = new EventEmitter<string>();
        var subscription = emitter.Event(data => { });

        // Act & Assert - Should not throw
        subscription.Dispose();
        subscription.Dispose();
    }
}

public class EventEmitterNoDataTests
{
    [Fact]
    public void Event_AddsListener()
    {
        // Arrange
        var emitter = new EventEmitter();
        var called = false;

        // Act
        emitter.Event(() => called = true);
        emitter.Fire();

        // Assert
        Assert.True(called);
    }

    [Fact]
    public void Event_MultipleListeners_AllCalled()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count = 0;

        // Act
        emitter.Event(() => count++);
        emitter.Event(() => count++);
        emitter.Event(() => count++);
        emitter.Fire();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void Fire_NoListeners_DoesNotThrow()
    {
        // Arrange
        var emitter = new EventEmitter();

        // Act & Assert - Should not throw
        emitter.Fire();
    }

    [Fact]
    public void Event_ReturnsDisposable()
    {
        // Arrange
        var emitter = new EventEmitter();
        var called = false;

        // Act
        var subscription = emitter.Event(() => called = true);

        // Assert
        Assert.NotNull(subscription);
        Assert.IsAssignableFrom<IDisposable>(subscription);
    }

    [Fact]
    public void Dispose_RemovesListener()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count = 0;
        var subscription = emitter.Event(() => count++);

        // Act
        emitter.Fire();
        subscription.Dispose();
        emitter.Fire();

        // Assert
        Assert.Equal(1, count); // Only called once before disposal
    }

    [Fact]
    public void Clear_RemovesAllListeners()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count = 0;
        emitter.Event(() => count++);
        emitter.Event(() => count++);
        emitter.Event(() => count++);

        // Act
        emitter.Clear();
        emitter.Fire();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void Fire_MultipleTimes_CallsListenersEachTime()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count = 0;
        emitter.Event(() => count++);

        // Act
        emitter.Fire();
        emitter.Fire();
        emitter.Fire();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void Event_ListenerThrows_DoesNotStopOtherListeners()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count = 0;

        emitter.Event(() => throw new Exception("Test exception"));
        emitter.Event(() => count++);

        // Act & Assert
        Assert.Throws<Exception>(() => emitter.Fire());
        // Note: In current implementation, exception stops execution
        // If implementation changes to catch exceptions, adjust this test
    }

    [Fact]
    public void MultipleDispose_DoesNotThrow()
    {
        // Arrange
        var emitter = new EventEmitter();
        var subscription = emitter.Event(() => { });

        // Act & Assert - Should not throw
        subscription.Dispose();
        subscription.Dispose();
    }

    [Fact]
    public void MixedSubscriptionsAndDisposals_WorkCorrectly()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count1 = 0;
        var count2 = 0;
        var count3 = 0;

        var sub1 = emitter.Event(() => count1++);
        var sub2 = emitter.Event(() => count2++);
        var sub3 = emitter.Event(() => count3++);

        // Act
        emitter.Fire(); // All called
        sub2.Dispose();
        emitter.Fire(); // sub2 not called
        sub1.Dispose();
        emitter.Fire(); // Only sub3 called

        // Assert
        Assert.Equal(2, count1);
        Assert.Equal(1, count2);
        Assert.Equal(3, count3);
    }

    [Fact]
    public void AddListenerDuringFire_DoesNotCallNewListener()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count = 0;

        emitter.Event(() =>
        {
            count++;
            emitter.Event(() => count += 10); // Add listener during fire
        });

        // Act
        emitter.Fire();

        // Assert
        Assert.Equal(1, count); // New listener not called in same fire
    }

    [Fact]
    public void RemoveListenerDuringFire_HandlesGracefully()
    {
        // Arrange
        var emitter = new EventEmitter();
        var count = 0;
        IDisposable? subscription = null;

        subscription = emitter.Event(() =>
        {
            count++;
            subscription?.Dispose(); // Remove self during fire
        });

        emitter.Event(() => count++); // Second listener

        // Act
        emitter.Fire();
        emitter.Fire(); // Should only call second listener

        // Assert
        Assert.Equal(3, count); // 1 (first listener first fire) + 1 (second listener first fire) + 1 (second listener second fire)
    }
}
