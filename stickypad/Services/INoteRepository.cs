using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StickyPad.Models;

namespace StickyPad.Services;

public interface INoteRepository
{
    /// 휴지통이 아닌(IsDeleted == false) 노트들을 ModifiedAt 내림차순으로 반환.
    Task<IReadOnlyList<Note>> GetAllAsync();

    /// 휴지통 항목만 DeletedAt 내림차순으로 반환.
    Task<IReadOnlyList<Note>> GetTrashedAsync();

    Task<Note?> GetByIdAsync(Guid id);
    Task<Note?> FindByTitleAsync(string title);

    /// 주어진 절대 경로에 연동된(휴지통이 아닌) 노트를 찾는다. 같은 .md 파일을 다시 열 때
    /// 중복 노트를 만들지 않고 기존 노트를 재사용하기 위한 조회. 경로 비교는 대소문자 무시.
    Task<Note?> FindByLinkedPathAsync(string path);

    /// 노트 문서를 통째로 삽입/교체. 휴지통 상태(IsDeleted/DeletedAt)까지 인자 그대로 반영한다.
    /// 백업 import·새 노트 생성처럼 "들어온 값이 곧 정답"인 경로 전용.
    Task UpsertAsync(Note note);

    /// 콘텐츠·서식·위치 등 편집 가능한 필드만 저장한다. 휴지통 상태(IsDeleted/DeletedAt)는
    /// DB에 이미 저장된 값을 보존 — 열린 노트 창의 옛 메모리 복사본이 삭제를 되돌리지 못하게 한다.
    /// 이미 영구 삭제된(DB에 없는) 노트는 재삽입하지 않고 무시한다 — 닫히는 창의 뒤늦은
    /// flush 가 지운 노트를 부활시키지 못하게 한다. 휴지통 전환·재삽입은 다루지 않으며,
    /// 그 역할은 오직 UpsertAsync/DeleteAsync/RestoreAsync/PurgeAsync 가 담당.
    Task SaveContentAsync(Note note);

    /// Soft delete — IsDeleted=true, DeletedAt=now.
    Task DeleteAsync(Guid id);

    /// 휴지통에서 일반 노트로 복구.
    Task RestoreAsync(Guid id);

    /// DB에서 완전히 제거 (휴지통 비우기 / 30일 경과 청소).
    Task PurgeAsync(Guid id);

    /// DeletedAt &lt; cutoff 인 모든 휴지통 항목을 영구 삭제. 삭제된 개수 반환.
    Task<int> PurgeTrashedOlderThanAsync(DateTime cutoff);
}
