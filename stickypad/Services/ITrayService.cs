using System;

namespace StickyPad.Services;

public interface ITrayService : IDisposable
{
    void Initialize();
}
