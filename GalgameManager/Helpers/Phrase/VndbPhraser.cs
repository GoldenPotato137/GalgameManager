using System.Collections.ObjectModel;
using System.Reflection;
using GalgameManager.Contracts.Phrase;
using GalgameManager.Enums;
using GalgameManager.Helpers.API;
using GalgameManager.Models;
using Newtonsoft.Json.Linq;

namespace GalgameManager.Helpers.Phrase;

public class VndbPhraser : IGalInfoPhraser, IGalStatusSync, IGalCharacterPhraser
{
    private VndbApi _vndbApi;

    private readonly Dictionary<int, JToken> _tagDb = new();
    private bool _init;
    private const string TagDbFile = @"Assets\Data\vndb-tags-latest.json";
    /// <summary>
    /// id eg:g530[1..]=530=(int)530
    /// </summary>
    private const string VndbFields = "title, titles.title, titles.lang, description, image.url, id, rating, length, " +
                                      "length_minutes, tags.id, tags.rating, developers.original, developers.name, released";

    private bool _authed;
    private Task? _checkAuthTask;

    public VndbPhraser()
    {
        _vndbApi = new VndbApi();
    }
    
    public VndbPhraser(VndbPhraserData data)
    {
        _vndbApi = new VndbApi();
        UpdateData(data);
    }
    
    public void UpdateData(IGalInfoPhraserData data)
    {
        if (data is VndbPhraserData vndbData)
        {
            _checkAuthTask = Task.Run(async () =>
            {
                _vndbApi.UpdateToken(vndbData.Token);
                try
                {
                    await _vndbApi.GetAuthInfo();
                    _authed = true;
                }
                catch (InvalidTokenException)
                {
                    _authed = false;
                    _vndbApi.UpdateToken(null);
                }
                catch (Exception)
                {
                    _authed = false; //todo:修复该phraser
                }
            });
        }
    }

    private async Task Init()
    {
        _init = true;
        Assembly assembly = Assembly.GetExecutingAssembly();
        var file = Path.Combine(Path.GetDirectoryName(assembly.Location)!, TagDbFile);
        if (!File.Exists(file)) return;

        JToken json = JToken.Parse(await File.ReadAllTextAsync(file));
        List<JToken>? tags = json.ToObject<List<JToken>>();
        tags!.ForEach(tag => _tagDb.Add(int.Parse(tag["id"]!.ToString()), tag));
    }

    private static async Task TryGetId(Galgame galgame)
    {
        if (string.IsNullOrEmpty(galgame.Ids[(int)RssType.Vndb]))
        {
            var id = await PhraseHelper.TryGetVndbIdAsync(galgame.Name!);
            if (id is not null)
            {
                galgame.Ids[(int)RssType.Vndb] = id.ToString();
            }
        }
    }
    
    public async Task<Galgame?> GetGalgameInfo(Galgame galgame)
    {
        if (!_init) await Init();
        Galgame result = new();
        try
        {
            // 试图离线获取ID
            await TryGetId(galgame);

            VndbResponse<VndbVn> vndbResponse;
            try
            {
                // with v
                var idString = galgame.Ids[(int)RssType.Vndb];
                if (string.IsNullOrEmpty(idString))
                {
                    vndbResponse = await _vndbApi.GetVisualNovelAsync(new VndbQuery
                    {
                        Fields = VndbFields,
                        Filters = VndbFilters.Equal("search", galgame.Name.Value!)
                    });
                }
                else
                {
                    if (!string.IsNullOrEmpty(idString) && idString[0] != 'v')
                        idString = "v"+idString;
                    vndbResponse = await _vndbApi.GetVisualNovelAsync(new VndbQuery
                    {
                        Fields = VndbFields,
                        Filters = VndbFilters.Equal("id", idString)
                    });
                    if (vndbResponse.Results is null || vndbResponse.Results.Count == 0)
                    {
                        vndbResponse = await _vndbApi.GetVisualNovelAsync(new VndbQuery
                        {
                            Fields = VndbFields,
                            Filters = VndbFilters.Equal("search", galgame.Name.Value!)
                        });
                    }
                }
            }
            catch (ThrottledException)
            {
                await Task.Delay(60 * 1000); // 1 minute
                vndbResponse = await _vndbApi.GetVisualNovelAsync(new VndbQuery
                    {
                        Fields = VndbFields,
                        Filters = VndbFilters.Equal("search", galgame.Name.Value!)
                    });
            }
            catch (Exception)
            {
                return null;
            }
            
            if (vndbResponse.Results is null || vndbResponse.Results.Count == 0) return null;
            VndbVn rssItem = vndbResponse.Results[0];
            result.Name = GetJapaneseName(rssItem.Titles) ?? rssItem.Title ?? Galgame.DefaultString;
            result.CnName = GetChineseName(rssItem.Titles);
            result.Description = rssItem.Description ?? Galgame.DefaultString;
            result.RssType = GetPhraseType();
            // id eg: v16044 -> 16044
            var id = rssItem.Id! ;
            result.Id = id.StartsWith("v")?id[1..]:id;
            result.Rating =(float)Math.Round(rssItem.Rating / 10 ?? 0.0D, 1);
            result.ExpectedPlayTime = GetLength(rssItem.Lenth,rssItem.LengthMinutes);
            result.ImageUrl = rssItem.Image != null ? rssItem.Image.Url! :"";
            // Developers
            if (rssItem.Developers?.Count > 0)
            {
                IEnumerable<string> developers = rssItem.Developers.Select<VndbProducer, string>(d =>
                    d.Original ?? d.Name ?? "");
                result.Developer = string.Join(",", developers);
            }else
            {
                result.Developer = Galgame.DefaultString;
            }

            result.ReleaseDate = (rssItem.Released != null
                ? IGalInfoPhraser.GetDateTimeFromString(rssItem.Released)
                : null) ?? DateTime.MinValue;
            // Tags
            result.Tags.Value = new ObservableCollection<string>();
            if (rssItem.Tags != null)
            {
                IOrderedEnumerable<VndbTag> tmpTags = rssItem.Tags.OrderByDescending(t => t.Rating);
                foreach (VndbTag tag in tmpTags)
                {
                    if (!int.TryParse(tag.Id![1..], out var i)) continue;
                    if (_tagDb.TryGetValue(i, out JToken? tagInfo))
                        result.Tags.Value.Add(tagInfo["name"]!.ToString() ?? "");
                }
            }
            // Characters
            try
            {
                VndbResponse<VndbCharacter> vndbCharacterResponse = await _vndbApi.GetVnCharacterAsync(new VndbQuery
                {
                    Filters = VndbFilters.Equal("vn", VndbFilters.Equal("id", id)),
                    Fields = "id, name, original, vns.id, vns.role"
                });
                if (vndbCharacterResponse.Results is not null && vndbResponse.Results.Count != 0)
                {
                    foreach (VndbCharacter character in vndbCharacterResponse.Results)
                    {
                        GalgameCharacter c = new()
                        {
                            Name = character.Original ?? character.Name ?? "",
                            Ids =
                            {
                                [(int)GetPhraseType()] =
                                    character.Id!.StartsWith("v") ? character.Id[1..] : character.Id
                            }
                        };
                        List<VndbVn.VndbRole?>? vns = character.Vns?.Where(vn => vn.Id == id).Select(vn => vn.Role)
                            .ToList();
                        if (vns is { Count: > 0 })
                        {
                            c.Relation = vns[0] switch
                            {
                                VndbVn.VndbRole.Main => "主角",
                                VndbVn.VndbRole.Primary => "主要人物",
                                VndbVn.VndbRole.Side => "次要人物",
                                VndbVn.VndbRole.Appears => "仅出现",
                                _ => "-"
                            };
                        }

                        result.Characters.Add(c);
                    }
                }
            }
            catch
            {
                return result;
            }
        }
        catch (Exception)
        {
            return null;
        }
        return result;
    }

    public RssType GetPhraseType() => RssType.Vndb;
    
    public async Task<GalgameCharacter?> GetGalgameCharacter(GalgameCharacter galgameCharacter)
    {
        var id = galgameCharacter.Ids[(int)GetPhraseType()];
        if (id == null) return null;
        return await GetCharacterById(id);
    }

    private async Task<GalgameCharacter?> GetCharacterById(string id)
    {
        VndbResponse<VndbCharacter> characterResponse = await _vndbApi.GetVnCharacterAsync(new VndbQuery
        {
            Fields =
                "id, name, original, aliases, description, image.url, blood_type, height, weight, bust, waist, hips, cup, age, birthday, sex, vns.id, vns.role",
            Filters = VndbFilters.Equal("id", id.StartsWith("c")?id:$"c{id}")
        });
        if (characterResponse.Count < 1 || characterResponse.Results == null ||
            characterResponse.Results.Count < 1) return null;
        VndbCharacter vnCharacter = characterResponse.Results[0];
        GalgameCharacter character = new()
        {
            Name = vnCharacter.Original ?? vnCharacter.Name ?? "",
            PreviewImageUrl = vnCharacter.Image?.Url,
            ImageUrl = vnCharacter.Image?.Url,
            Summary = vnCharacter.Description ?? "-",
            Gender = vnCharacter.Sex?[1] switch
            {
                "m" => Gender.Male,
                "f" => Gender.Female,
                _ => Gender.Unknown
            },
            Height = vnCharacter.Height!=null?$"{vnCharacter.Height}cm":"-", 
            Weight = vnCharacter.Weight!=null?$"{vnCharacter.Weight}cm":"-",
            BWH = vnCharacter.Bust!=null?$"B{vnCharacter.Bust}({vnCharacter.Cup})/W{vnCharacter.Waist}/H{vnCharacter.Hips}":"-",
            BloodType = vnCharacter.BloodType,
            BirthMon = vnCharacter.Birthday?[0],
            BirthDay = vnCharacter.Birthday?[1],
            BirthDate = vnCharacter.Birthday != null ? $"{vnCharacter.Birthday?[0]}月{vnCharacter.Birthday?[1]}日":"-"
        };
        return character;
    }

    private static string GetChineseName(IReadOnlyCollection<VndbTitle>? titles)
    {
        if (titles == null) return "";
        VndbTitle? title = titles.FirstOrDefault(t => t.Lang == "zh-Hans") ??
                           titles.FirstOrDefault(t => t.Lang == "zh-Hant");
        return title?.Title!;
    }

    private static string GetJapaneseName(IReadOnlyCollection<VndbTitle>? titles)
    {
        if (titles == null) return "";
        VndbTitle? title = titles.FirstOrDefault(t => t.Lang == "ja");
        return title?.Title ?? null;
    }
    private static string GetLength(VndbVn.VnLenth? length, int? lengthMinutes)
    {
        if (lengthMinutes != null)
        {
            return (lengthMinutes > 60?lengthMinutes / 60 + "h":"") + (lengthMinutes%60 != 0?lengthMinutes % 60 + "m":"");
        }

        if (length == null) return Galgame.DefaultString;
        return length switch
        {
            VndbVn.VnLenth.VeryShort => "very short",
            VndbVn.VnLenth.Short => "short",
            VndbVn.VnLenth.Medium => "medium",
            VndbVn.VnLenth.Long => "long",
            VndbVn.VnLenth.VeryLong => "very long",
            _ => Galgame.DefaultString
        };
    }

    public async Task<GalgameCharacter?> GetGalgameCharacterByName(string name)
    {
        VndbResponse<VndbCharacter> characterResponse = await _vndbApi.GetVnCharacterAsync(new VndbQuery
        {
            Fields =
                "id, name, original, aliases, description, image.url, blood_type, height, weight, bust, waist, hips, cup, age, birthday, sex, vns.id, vns.role",
            Filters = VndbFilters.Equal("search", name)
        });
        if (characterResponse.Count < 1 || characterResponse.Results == null ||
            characterResponse.Results.Count < 1) return null;
        VndbCharacter vnCharacter = characterResponse.Results[0];
        GalgameCharacter character = new()
        {
            Name = vnCharacter.Name ?? "",
            PreviewImageUrl = vnCharacter.Image?.Url,
            ImageUrl = vnCharacter.Image?.Url,
            Summary = vnCharacter.Description ?? "",
            Gender = vnCharacter.Sex?[1] switch
            {
                "m" => Gender.Male,
                "f" => Gender.Female,
                _ => Gender.Unknown
            },
            Height = $"{vnCharacter.Height}cm", 
            Weight = $"{vnCharacter.Weight}cm",
            BWH = $"B{vnCharacter.Bust}({vnCharacter.Cup})/W{vnCharacter.Waist}/H{vnCharacter.Hips}",
            BloodType = vnCharacter.BloodType,
            BirthMon = vnCharacter.Birthday?[0],
            BirthDay = vnCharacter.Birthday?[1],
            BirthDate = vnCharacter.Birthday != null ? $"{vnCharacter.Birthday?[0]}月{vnCharacter.Birthday?[1]}日":"-"
        };
        return character;
    }

    public async Task<(GalStatusSyncResult, string)> UploadAsync(Galgame galgame)
    {
        if (_checkAuthTask != null) await _checkAuthTask;
        if (!_authed) return (GalStatusSyncResult.UnAuthorized, "VndbPhraser_UnAuthorized".GetLocalized());
        if (string.IsNullOrEmpty(galgame.Ids[(int)RssType.Vndb]))
            return (GalStatusSyncResult.NoId, "VndbPhraser_NoId".GetLocalized());
        var id = galgame.Ids[(int)RssType.Vndb]!.StartsWith("v")
            ? galgame.Ids[(int)RssType.Vndb]!
            : "v" + galgame.Ids[(int)RssType.Vndb]!;
        
        try
        {
            // 先尝试读取
            VndbResponse<VndbUserListItem> tryGetResponse = await _vndbApi.GetUserVisualNovelListAsync(new VndbQuery
            {
                Fields = "vote, labels.id", Filters = VndbFilters.Equal("id", id)
            });
            var labelSet = galgame.PlayType.ToVndbCollectionType();
            PatchUserListRequest patchUserListRequest = new()
            {
                LabelsSet = new List<int> {labelSet},
                Notes = galgame.Comment,
                Vote = galgame.MyRate == 0 ? null : galgame.MyRate * 10 // BgmRate: 0~10, VndbRate: 10~100, vndb的一个奇怪的点, 它网站上是 0~10
                // Vndb无private选项
            };
            if (tryGetResponse.Results?.Count == 1)
            {
                patchUserListRequest.LabelsUnset = new List<int>();
                // 去除旧标签
                foreach (UserLabel userListItem in tryGetResponse.Results![0].Labels!)
                {
                    if (userListItem.Id is <= 6 and >= 1 && userListItem.Id != labelSet)
                        patchUserListRequest.LabelsUnset.Add(userListItem.Id);
                }
            }

            await _vndbApi.ModifyUserVnAsync(id, patchUserListRequest);
        }
        catch (Exception e)
        {
            return (GalStatusSyncResult.Other, e.Message);
        }
        return (GalStatusSyncResult.Ok, "VndbPhraser_UploadAsync_Success".GetLocalized());
    }

    public async Task<(GalStatusSyncResult, string)> DownloadAsync(Galgame galgame)
    {
        if (_checkAuthTask != null) await _checkAuthTask;
        if (!_authed) return (GalStatusSyncResult.UnAuthorized, "VndbPhraser_UnAuthorized".GetLocalized());
        if (string.IsNullOrEmpty(galgame.Ids[(int)RssType.Vndb]))
            return (GalStatusSyncResult.NoId, "VndbPhraser_NoId".GetLocalized());
        var id = galgame.Ids[(int)RssType.Vndb]!.StartsWith("v")
            ? galgame.Ids[(int)RssType.Vndb]!
            : "v" + galgame.Ids[(int)RssType.Vndb]!;
        try
        {
            VndbResponse<VndbUserListItem> response = await _vndbApi.GetUserVisualNovelListAsync(new VndbQuery
            {
                Fields = "vote, labels.id, notes", Filters = VndbFilters.Equal("id", id)
            });

            if (response.Results?.Count != 1)
                return (GalStatusSyncResult.Ok, "VndbPhraser_DownloadAsync_Success".GetLocalized());

            VndbUserListItem r = response.Results[0];
            if (r.Vote.HasValue) galgame.MyRate = r.Vote.Value / 10;
            if (r.Notes != null) galgame.Description = r.Notes;
            if (r.Labels != null) galgame.PlayType = r.Labels.First(l=>l.Id is <= 6 and >= 1).Id.VndbCollectionTypeToPlayType();
        }
        catch (Exception e)
        {
            return (GalStatusSyncResult.Other, e.Message);
        }
        return (GalStatusSyncResult.Ok, "VndbPhraser_DownloadAsync_Success".GetLocalized());

    }
    
    public async Task<(GalStatusSyncResult, string)> DownloadAllAsync(IList<Galgame> galgames)
    {
        if (_checkAuthTask != null) await _checkAuthTask;
        if (!_authed) return (GalStatusSyncResult.UnAuthorized, "VndbPhraser_UnAuthorized".GetLocalized());
        try
        {
            VndbResponse<VndbUserListItem> response = await _vndbApi.GetUserVisualNovelListAsync(new VndbQuery
            {
                Fields = "vote, labels.id, notes"
            });
            if (response.Results == null || response.Results.Count == 0) return (GalStatusSyncResult.Ok, "VndbPhraser_UploadAsync_Success".GetLocalized());
            foreach (VndbUserListItem listItem in response.Results)
            {
                Galgame? galgame = galgames.FirstOrDefault(g => g.Ids[(int)RssType.Bangumi] == listItem.Id?[1..]);
                if (galgame == null)continue;
                if (listItem.Vote.HasValue) galgame.MyRate = listItem.Vote.Value / 10;
                if (listItem.Notes != null) galgame.Description = listItem.Notes;
                if (listItem.Labels != null) galgame.PlayType = listItem.Labels.First(l=>l.Id is <= 6 and >= 1).Id.VndbCollectionTypeToPlayType();
            }
        }
        catch (Exception e)
        {
            return (GalStatusSyncResult.Other, e.Message);
        }
        return (GalStatusSyncResult.Ok, "VndbPhraser_DownloadAsync_Success".GetLocalized());
    }
}

public class VndbPhraserData : IGalInfoPhraserData
{
    public string? Token;

    public VndbPhraserData() { }
    
    public VndbPhraserData(string? token)
    {
        Token = token;
    }
}
