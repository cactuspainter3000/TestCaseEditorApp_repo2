using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class JamaDraftPublicationTests
{
    [TestMethod]
    public async Task Discovery_ReadsProjectTypesAndCompatibleSetsAcrossPagesWithoutWrites()
    {
        var handler = new DraftHandler(request =>
        {
            var url = request.RequestUri!.PathAndQuery;
            if (url.Contains("itemtypes"))
                return Json(new { data = new[] { new { id = 194, display = "Verification Case" }, new { id = 193, display = "Requirement" } } });
            if (url.Contains("relationshiptypes"))
                return Json(new { data = new[] { new { id = 7, name = "Verifies" } } });
            if (url.Contains("startAt=0"))
                return Json(new
                {
                    data = Enumerable.Range(1, 50).Select(id => new { id, fields = new { name = $"Requirement {id}" } }),
                    meta = new { pageInfo = new { totalResults = 51 } }
                });
            return Json(new
            {
                data = new[] { new { id = 501, documentKey = "DEMO-SET-1", childItemType = 194, fields = new { name = "Verification" } } },
                meta = new { pageInfo = new { totalResults = 51 } }
            });
        });
        var options = await Service(handler).GetDraftPublicationOptionsAsync(42);
        Assert.AreEqual(194, options.ItemTypes.Single().Id);
        Assert.AreEqual(501, options.Containers.Single().Id);
        Assert.AreEqual(7, options.RelationshipTypes.Single().Id);
        Assert.IsTrue(handler.Calls.All(call => call.Method == HttpMethod.Get));
    }

    [TestMethod]
    [DataRow(42, 193)]
    [DataRow(99, 194)]
    public async Task Creation_RejectsIncompatibleDestinationWithoutWrites(int project, int childItemType)
    {
        var handler = new DraftHandler(_ => Json(new
        {
            data = new { id = 501, project, childItemType, fields = new { name = "Other set", childItemType = 194 } }
        }));

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Service(handler).CreateDraftTestCaseAsync(42, 501, 194, "Draft", "Draft text"));

        Assert.IsTrue(handler.Calls.All(call => call.Method == HttpMethod.Get));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Creation_UsesOnlyExplicitDestinationAndNeverFallsBackOrRetries(bool accepted)
    {
        var handler = new DraftHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(new { data = new { id = 501, project = 42, childItemType = 194, fields = new { name = "Verification" } } });
            return accepted ? Json(new { meta = new { id = 900 } }) : new HttpResponseMessage(HttpStatusCode.BadRequest);
        });
        var service = Service(handler);
        if (accepted)
            Assert.AreEqual(900, await service.CreateDraftTestCaseAsync(42, 501, 194, "[AI DRAFT] Warning", "A < B"));
        else
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                service.CreateDraftTestCaseAsync(42, 501, 194, "[AI DRAFT] Warning", "A < B"));
        var post = handler.Calls.Single(call => call.Method == HttpMethod.Post);
        using var body = JsonDocument.Parse(post.Body!);
        Assert.AreEqual(501, body.RootElement.GetProperty("location").GetProperty("parent").GetInt32());
        Assert.AreEqual(194, body.RootElement.GetProperty("itemType").GetInt32());
        StringAssert.Contains(body.RootElement.GetProperty("fields").GetProperty("description").GetString()!, "A &lt; B");
    }

    [TestMethod]
    public async Task Preflight_RejectsCrossProjectRequirementBeforeAnyWrite()
    {
        var handler = new DraftHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/501")
                ? Json(new { data = new { id = 501, project = 42, childItemType = 194, fields = new { name = "Verification" } } })
                : Json(new { data = new { id = 123, project = 99 } }));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            Service(handler).ValidateDraftDestinationAsync(42, 501, 194, [123]));
        Assert.IsTrue(handler.Calls.All(call => call.Method == HttpMethod.Get));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Linking_UsesExplicitDirectionAndAvoidsRecreatingExistingLink(bool existing)
    {
        var handler = new DraftHandler(request =>
        {
            var url = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post) return Json(new { meta = new { id = 1000 } });
            if (url.EndsWith("relationshiptypes")) return Json(new { data = new[] { new { id = 7, name = "Verifies" } } });
            if (url.EndsWith("downstreamrelationships"))
                return existing ? Json(new { data = new[] { new { id = 1000, fromItem = 123, toItem = 900, relationshipType = 7 } } })
                    : Json(new { data = Array.Empty<object>() });
            return Json(new { data = new { id = 123, project = 42 } });
        });
        await Service(handler).LinkDraftTestCaseAsync(42, 123, 900, 7);
        Assert.AreEqual(existing ? 0 : 1, handler.Calls.Count(call => call.Method == HttpMethod.Post));
        if (!existing)
        {
            using var body = JsonDocument.Parse(handler.Calls.Single(call => call.Method == HttpMethod.Post).Body!);
            Assert.AreEqual(123, body.RootElement.GetProperty("fromItem").GetInt32());
            Assert.AreEqual(900, body.RootElement.GetProperty("toItem").GetInt32());
        }
    }

    [TestMethod]
    public async Task Creation_WithoutConfirmedIdFailsExplicitly()
    {
        var handler = new DraftHandler(request => request.Method == HttpMethod.Get
            ? Json(new { data = new { project = 42, childItemType = 194, fields = new { name = "Verification" } } })
            : Json(new { meta = new { status = "Created" } }));
        await Assert.ThrowsExceptionAsync<System.IO.InvalidDataException>(() =>
            Service(handler).CreateDraftTestCaseAsync(42, 501, 194, "Draft", "Draft text"));
        Assert.AreEqual(1, handler.Calls.Count(call => call.Method == HttpMethod.Post));
    }

    private static JamaConnectService Service(DraftHandler handler) =>
        new("https://jama.invalid", "fake-test-token", new HttpClient(handler));

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed class DraftHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string? Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
