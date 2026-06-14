using PersonalAI.Core.Tools;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Workspaces;

public sealed class WorkspaceRegistrationService(
    IWorkspaceRepository repository,
    IWorkspaceRegistry registry,
    IWorkspacePermissionInvalidator? permissionInvalidator = null)
    : IWorkspaceRegistrationService
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await repository.InitializeAsync(cancellationToken);
        await RevalidateAllAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PersistedWorkspace>> ListAsync(
        CancellationToken cancellationToken = default) =>
        repository.ListAsync(includeRemoved: false, cancellationToken);

    public async Task<PersistedWorkspace> RegisterAsync(
        string rootPath,
        string displayName,
        string source,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var descriptor = WorkspaceRegistry.CreateDescriptor(
            WorkspaceId.NewId(),
            rootPath,
            SanitizeDisplayName(displayName),
            string.IsNullOrWhiteSpace(source) ? "User" : source.Trim(),
            now);

        var existing = await repository.FindActiveByCanonicalRootAsync(
            descriptor.CanonicalRootPath,
            cancellationToken);
        if (existing is not null)
        {
            var revalidatedExisting = ValidatePersisted(existing with
            {
                DisplayName = descriptor.DisplayName,
                Source = descriptor.Source ?? existing.Source
            });
            return await ApplyValidationAsync(
                revalidatedExisting,
                cancellationToken);
        }

        var persisted = new PersistedWorkspace(
            descriptor.Id,
            descriptor.DisplayName,
            descriptor.CanonicalRootPath,
            descriptor.Source ?? "User",
            descriptor.RegisteredAtUtc,
            now,
            WorkspaceRegistrationStatus.Available,
            null,
            descriptor.Policy.IsReadOnly);

        return await ApplyValidationAsync(persisted, cancellationToken);
    }

    public async Task RemoveAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        await repository.RemoveAsync(
            workspaceId,
            DateTimeOffset.UtcNow,
            cancellationToken);
        registry.Remove(workspaceId);
        permissionInvalidator?.InvalidateWorkspacePermissions(workspaceId);
    }

    public async Task<PersistedWorkspace?> UpdateDisplayNameAsync(
        WorkspaceId workspaceId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var workspace = await repository.GetAsync(workspaceId, cancellationToken);
        if (workspace is null || !workspace.IsActive)
        {
            return null;
        }

        var updated = workspace with
        {
            DisplayName = SanitizeDisplayName(displayName)
        };

        if (updated.Status == WorkspaceRegistrationStatus.Available)
        {
            var applied = await ApplyValidationAsync(
                ValidatePersisted(updated),
                cancellationToken);
            return applied;
        }

        await repository.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<PersistedWorkspace?> RevalidateAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await repository.GetAsync(workspaceId, cancellationToken);
        if (workspace is null || !workspace.IsActive)
        {
            return null;
        }

        return await ApplyValidationAsync(
            ValidatePersisted(workspace),
            cancellationToken);
    }

    public async Task RevalidateAllAsync(CancellationToken cancellationToken = default)
    {
        var workspaces = await repository.ListAsync(
            includeRemoved: false,
            cancellationToken);

        foreach (var workspace in workspaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _ = await ApplyValidationAsync(
                    ValidatePersisted(workspace),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WorkspaceAccessException)
            {
                var invalid = MarkInvalid(
                    workspace,
                    WorkspaceRegistrationStatus.ValidationFailed,
                    "workspace_revalidation_failed");
                try
                {
                    await repository.UpsertAsync(invalid, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }

                RemoveRuntimeAndInvalidate(workspace.Id);
            }
            catch
            {
                var invalid = MarkInvalid(
                    workspace,
                    WorkspaceRegistrationStatus.ValidationFailed,
                    "workspace_revalidation_failed");
                try
                {
                    await repository.UpsertAsync(invalid, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }

                RemoveRuntimeAndInvalidate(workspace.Id);
            }
        }
    }

    private async Task<PersistedWorkspace> ApplyValidationAsync(
        PersistedWorkspace workspace,
        CancellationToken cancellationToken)
    {
        if (workspace.Status != WorkspaceRegistrationStatus.Available)
        {
            await repository.UpsertAsync(workspace, cancellationToken);
            RemoveRuntimeAndInvalidate(workspace.Id);
            return workspace;
        }

        var runtimeStatus = TryRegisterRuntime(workspace);
        if (runtimeStatus is not null)
        {
            var invalid = MarkInvalid(
                workspace,
                WorkspaceRegistrationStatus.ValidationFailed,
                runtimeStatus);
            await repository.UpsertAsync(invalid, cancellationToken);
            RemoveRuntimeAndInvalidate(workspace.Id);
            return invalid;
        }

        try
        {
            await repository.UpsertAsync(workspace, cancellationToken);
            return workspace;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            RemoveRuntimeAndInvalidate(workspace.Id);
            throw;
        }
    }

    private PersistedWorkspace ValidatePersisted(PersistedWorkspace workspace)
    {
        try
        {
            var descriptor = WorkspaceRegistry.CreateDescriptor(
                workspace.Id,
                workspace.CanonicalRootPath,
                workspace.DisplayName,
                workspace.Source,
                workspace.AddedAtUtc);

            if (!WorkspaceRegistry.PathComparer.Equals(
                    descriptor.CanonicalRootPath,
                    workspace.CanonicalRootPath))
            {
                return MarkInvalid(
                    workspace,
                    WorkspaceRegistrationStatus.NeedsReview,
                    "canonical_path_changed");
            }

            return workspace with
            {
                DisplayName = descriptor.DisplayName,
                CanonicalRootPath = descriptor.CanonicalRootPath,
                LastValidatedAtUtc = DateTimeOffset.UtcNow,
                Status = WorkspaceRegistrationStatus.Available,
                SafeStatusCode = null,
                IsReadOnly = descriptor.Policy.IsReadOnly,
                RemovedAtUtc = null
            };
        }
        catch (WorkspaceAccessException exception)
        {
            return MarkInvalid(
                workspace,
                MapStatus(exception.SafeErrorCode),
                exception.SafeErrorCode);
        }
    }

    private string? TryRegisterRuntime(PersistedWorkspace workspace)
    {
        if (workspace.Status != WorkspaceRegistrationStatus.Available ||
            !workspace.IsActive)
        {
            return null;
        }

        try
        {
            registry.Register(
                workspace.Id,
                workspace.CanonicalRootPath,
                workspace.DisplayName,
                workspace.Source);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceAccessException exception)
        {
            return exception.SafeErrorCode;
        }
        catch
        {
            return "workspace_runtime_registration_failed";
        }
    }

    private void RemoveRuntimeAndInvalidate(WorkspaceId workspaceId)
    {
        registry.Remove(workspaceId);
        permissionInvalidator?.InvalidateWorkspacePermissions(workspaceId);
    }

    private static PersistedWorkspace MarkInvalid(
        PersistedWorkspace workspace,
        WorkspaceRegistrationStatus status,
        string safeStatusCode) =>
        workspace with
        {
            LastValidatedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
            SafeStatusCode = safeStatusCode
        };

    private static WorkspaceRegistrationStatus MapStatus(string safeErrorCode) =>
        safeErrorCode switch
        {
            "workspace_not_found" => WorkspaceRegistrationStatus.Missing,
            "workspace_access_denied" => WorkspaceRegistrationStatus.AccessDenied,
            "reparse_point_rejected" => WorkspaceRegistrationStatus.UnsafeReparsePoint,
            "invalid_workspace_root" => WorkspaceRegistrationStatus.NeedsReview,
            "workspace_root_is_file" => WorkspaceRegistrationStatus.NeedsReview,
            _ => WorkspaceRegistrationStatus.ValidationFailed
        };

    private static string SanitizeDisplayName(string displayName)
    {
        var normalized = string.IsNullOrWhiteSpace(displayName)
            ? "Workspace"
            : displayName.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }
}
