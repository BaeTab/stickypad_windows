using System;

namespace StickyPad.Services;

/// 볼트 폴더의 외부 변경(.md)을 실행 중 자동 반영하는 감시자(볼트 모드 전용).
public interface IVaultWatcher : IDisposable
{
    /// 감시 시작. FSW 활성화 직후 정합 사이클 1회를 돌려 시작~감시 사이의 변경을 회수한다.
    void Start(string vaultFolder);
}
