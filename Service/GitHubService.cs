using Microsoft.Extensions.Options;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Service
{
    public class GitHubService : IGitHubService
    {
        private readonly GitHubClient _client;
        private readonly GitHubIntegrationOptions _options;

        public GitHubService(IOptions<GitHubIntegrationOptions> options)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _client = new GitHubClient(new ProductHeaderValue("GitHubPortfolio"))
            {
                Credentials = new Credentials(_options.Token ?? throw new ArgumentNullException(nameof(_options.Token), "GitHub Personal Access Token is not set."))
            };
        }

        public async Task<List<RepositoryDetails>> GetPortfolio()
        {
            if (string.IsNullOrWhiteSpace(_options.Name))
            {
                throw new ArgumentException("GitHub username is not provided.");
            }

            return await FetchRepositories(_options.Name);
        }

        public async Task<int> GetUserFollowersAsync(string userName)
        {
            return (await _client.User.Followers.GetAll(userName)).Count;
        }

        public async Task<List<RepositoryDetails>> SearchRepositoriesAsync(string? query = null, string? language = null, string? user = null)
        {
            var searchQuery = new List<string>
            {
                !string.IsNullOrWhiteSpace(query) ? query : "",
                !string.IsNullOrWhiteSpace(user) ? $"user:{user}" : "",
                "stars:>=0"
            }.Where(s => !string.IsNullOrEmpty(s));

            var request = new SearchRepositoriesRequest(string.Join(" ", searchQuery));
            var repositories = (await _client.Search.SearchRepo(request)).Items ?? new List<Repository>();

            return await ProcessRepositories(repositories, language);
        }

        private async Task<List<RepositoryDetails>> FetchRepositories(string userName)
        {
            return await ProcessRepositories(await _client.Repository.GetAllForUser(userName));
        }

        private async Task<List<RepositoryDetails>> ProcessRepositories(IEnumerable<Repository> repositories, string? language = null)
        {
            var filteredRepositories = string.IsNullOrWhiteSpace(language)
                ? repositories
                : await FilterRepositoriesByLanguage(repositories, language);

            var tasks = filteredRepositories.Select(async repo => new RepositoryDetails
            {
                Name = repo.Name,
                Description = repo.Description,
                Stars = repo.StargazersCount,
                LastCommit = repo.PushedAt?.DateTime ?? repo.UpdatedAt.DateTime,
                Languages = (await _client.Repository.GetAllLanguages(repo.Owner.Login, repo.Name)).Select(l => l.Name).ToList(),
                PullRequests = (await _client.Repository.PullRequest.GetAllForRepository(repo.Owner.Login, repo.Name)).Count,
                HtmlUrl = repo.HtmlUrl,
                Owner = repo.Owner.Login,
                Language = repo.Language
            });

            return await Task.WhenAll(tasks).ContinueWith(t => t.Result.ToList());
        }

        private async Task<IEnumerable<Repository>> FilterRepositoriesByLanguage(IEnumerable<Repository> repositories, string language)
        {
            var languagesToSearch = language.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var filteredTasks = repositories.Select(async repo =>
            {
                var repoLanguages = await _client.Repository.GetAllLanguages(repo.Owner.Login, repo.Name);
                return languagesToSearch.All(lang => repoLanguages.Any(rl => string.Equals(rl.Name, lang, StringComparison.OrdinalIgnoreCase))) ? repo : null;
            });

            return (await Task.WhenAll(filteredTasks)).Where(repo => repo != null)!;
        }
    }
}

