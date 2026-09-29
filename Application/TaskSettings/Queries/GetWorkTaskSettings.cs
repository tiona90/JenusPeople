using Application.Core;
using MediatR;
using Persistence;

namespace Application.TaskSettings.Queries;

public class GetWorkTaskSettings
{
    public class Query : IRequest<Result<WorkTaskSettingsDto>>;

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<WorkTaskSettingsDto>>
    {
        public async Task<Result<WorkTaskSettingsDto>> Handle(Query request, CancellationToken cancellationToken) =>
            Result<WorkTaskSettingsDto>.Success(WorkTaskSettingsDto.From(await WorkTaskSettingsStore.LoadAsync(context, cancellationToken)));
    }
}
