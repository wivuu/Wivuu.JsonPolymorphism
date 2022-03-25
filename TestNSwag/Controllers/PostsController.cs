namespace TestNSwag.Controllers;

[ApiController]
[Route("[controller]")]
public class PostsController : ControllerBase
{
    private static List<Models.Post> Posts = new ();

    public PostsController()
    {
    }

    [HttpGet]
    public async IAsyncEnumerable<Models.Post> GetPosts()
    {
        await Task.Yield();

        foreach (var post in Posts)
        {
            yield return post;
        }
    }

    [HttpPost]
    public async Task<ActionResult<Models.Post>> AddPost(Models.Post post)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        await Task.Yield();
        Posts.Add(post);
        return post;
    }
}