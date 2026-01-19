using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace UIProbe
{
    /// <summary>
    /// UI 问题类型
    /// </summary>
    public enum UIProblemType
    {
        Warning,    // 警告
        Error       // 错误
    }

    /// <summary>
    /// 单个 UI 问题
    /// </summary>
    public class UIProblem
    {
        public UIProblemType Type;
        public string RuleName;
        public string Description;
        public GameObject Target;
        public string NodePath;
        
        public Color GetColor()
        {
            return Type == UIProblemType.Error 
                ? new Color(0.9f, 0.3f, 0.3f) 
                : new Color(0.9f, 0.7f, 0.2f);
        }
        
        public string GetIcon()
        {
            return Type == UIProblemType.Error ? "X" : "!";
        }
    }

    /// <summary>
    /// UI 检测规则接口
    /// </summary>
    public interface IUICheckRule
    {
        string RuleName { get; }
        string Description { get; }
        bool IsEnabled { get; set; }
        List<UIProblem> Check(GameObject root);
    }

    /// <summary>
    /// UI 问题检测器
    /// </summary>
    public static class UIProbeChecker
    {
        private static List<IUICheckRule> _rules;
        
        public static List<IUICheckRule> Rules
        {
            get
            {
                if (_rules == null)
                {
                    _rules = new List<IUICheckRule>
                    {
                        new MissingImageSpriteRule(),
                        new MissingTextFontRule(),
                        new UnnecessaryRaycastTargetRule(),
                        new BadNamingRule(),
                        new EmptyTextRule(),
                        new MissingCanvasGroupRule(),
                        new DuplicateNameRule()
                    };
                }
                return _rules;
            }
        }
        
        public static List<UIProblem> CheckAll(GameObject root)
        {
            var problems = new List<UIProblem>();
            
            foreach (var rule in Rules)
            {
                if (rule.IsEnabled)
                {
                    problems.AddRange(rule.Check(root));
                }
            }
            
            return problems;
        }
        
        public static List<UIProblem> CheckSingle(GameObject target)
        {
            var problems = new List<UIProblem>();
            
            foreach (var rule in Rules)
            {
                if (rule.IsEnabled)
                {
                    problems.AddRange(CheckNode(target, rule));
                }
            }
            
            return problems;
        }
        
        private static List<UIProblem> CheckNode(GameObject go, IUICheckRule rule)
        {
            var problems = new List<UIProblem>();
            var result = rule.Check(go);
            problems.AddRange(result);
            return problems;
        }
        
        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }

    #region 检测规则实现

    /// <summary>
    /// 检测 Image 组件缺少 Sprite
    /// </summary>
    public class MissingImageSpriteRule : IUICheckRule
    {
        public string RuleName => "Image 缺少 Sprite";
        public string Description => "Image 组件的 Source Image 为空";
        public bool IsEnabled { get; set; } = true;
        
        public List<UIProblem> Check(GameObject root)
        {
            var problems = new List<UIProblem>();
            var images = root.GetComponentsInChildren<Image>(true);
            
            foreach (var img in images)
            {
                if (img.sprite == null && img.color.a > 0)
                {
                    problems.Add(new UIProblem
                    {
                        Type = UIProblemType.Warning,
                        RuleName = RuleName,
                        Description = $"Image '{img.name}' 没有设置 Sprite",
                        Target = img.gameObject,
                        NodePath = GetPath(img.transform)
                    });
                }
            }
            
            return problems;
        }
        
        private string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }

    /// <summary>
    /// 检测 Text 组件缺少字体
    /// </summary>
    public class MissingTextFontRule : IUICheckRule
    {
        public string RuleName => "Text 缺少字体";
        public string Description => "Text 组件的 Font 为空";
        public bool IsEnabled { get; set; } = true;
        
        public List<UIProblem> Check(GameObject root)
        {
            var problems = new List<UIProblem>();
            var texts = root.GetComponentsInChildren<Text>(true);
            
            foreach (var txt in texts)
            {
                if (txt.font == null)
                {
                    problems.Add(new UIProblem
                    {
                        Type = UIProblemType.Error,
                        RuleName = RuleName,
                        Description = $"Text '{txt.name}' 缺少字体",
                        Target = txt.gameObject,
                        NodePath = GetPath(txt.transform)
                    });
                }
            }
            
            return problems;
        }
        
        private string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }

    /// <summary>
    /// 检测不必要的 Raycast Target
    /// </summary>
    public class UnnecessaryRaycastTargetRule : IUICheckRule
    {
        public string RuleName => "不必要的 Raycast Target";
        public string Description => "非交互元素开启了 Raycast Target，可能影响性能";
        public bool IsEnabled { get; set; } = true;
        
        public List<UIProblem> Check(GameObject root)
        {
            var problems = new List<UIProblem>();
            var graphics = root.GetComponentsInChildren<Graphic>(true);
            
            foreach (var g in graphics)
            {
                // 如果开启了 raycastTarget 但没有任何交互组件
                if (g.raycastTarget)
                {
                    var go = g.gameObject;
                    bool hasInteraction = go.GetComponent<Button>() != null ||
                                          go.GetComponent<Toggle>() != null ||
                                          go.GetComponent<Slider>() != null ||
                                          go.GetComponent<InputField>() != null ||
                                          go.GetComponent<ScrollRect>() != null ||
                                          go.GetComponent<Selectable>() != null;
                    
                    if (!hasInteraction)
                    {
                        problems.Add(new UIProblem
                        {
                            Type = UIProblemType.Warning,
                            RuleName = RuleName,
                            Description = $"'{g.name}' 开启了 Raycast Target 但无交互组件",
                            Target = go,
                            NodePath = GetPath(g.transform)
                        });
                    }
                }
            }
            
            return problems;
        }
        
        private string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }

    /// <summary>
    /// 检测不规范命名
    /// </summary>
    public class BadNamingRule : IUICheckRule
    {
        public string RuleName => "不规范命名";
        public string Description => "节点命名包含 (Clone)、(1) 等不规范后缀";
        public bool IsEnabled { get; set; } = true;
        
        private static readonly string[] BadPatterns = new[]
        {
            "(Clone)", "(1)", "(2)", "(3)", "(4)", "(5)",
            "GameObject", "Image", "Text", "Button", "Panel"
        };
        
        public List<UIProblem> Check(GameObject root)
        {
            var problems = new List<UIProblem>();
            var transforms = root.GetComponentsInChildren<Transform>(true);
            
            foreach (var t in transforms)
            {
                foreach (var pattern in BadPatterns)
                {
                    if (t.name.Contains(pattern) || t.name == pattern)
                    {
                        problems.Add(new UIProblem
                        {
                            Type = UIProblemType.Warning,
                            RuleName = RuleName,
                            Description = $"'{t.name}' 命名不规范 (含 '{pattern}')",
                            Target = t.gameObject,
                            NodePath = GetPath(t)
                        });
                        break;
                    }
                }
            }
            
            return problems;
        }
        
        private string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }

    /// <summary>
    /// 检测空 Text
    /// </summary>
    public class EmptyTextRule : IUICheckRule
    {
        public string RuleName => "空 Text 内容";
        public string Description => "Text 组件文本为空";
        public bool IsEnabled { get; set; } = false;  // 默认关闭，因为有些是动态填充
        
        public List<UIProblem> Check(GameObject root)
        {
            var problems = new List<UIProblem>();
            var texts = root.GetComponentsInChildren<Text>(true);
            
            foreach (var txt in texts)
            {
                if (string.IsNullOrEmpty(txt.text))
                {
                    problems.Add(new UIProblem
                    {
                        Type = UIProblemType.Warning,
                        RuleName = RuleName,
                        Description = $"Text '{txt.name}' 内容为空",
                        Target = txt.gameObject,
                        NodePath = GetPath(txt.transform)
                    });
                }
            }
            
            return problems;
        }
        
        private string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }

    /// <summary>
    /// 检测缺少 CanvasGroup
    /// </summary>
    public class MissingCanvasGroupRule : IUICheckRule
    {
        public string RuleName => "根节点缺少 CanvasGroup";
        public string Description => "UI 根节点建议添加 CanvasGroup 以便统一控制 Alpha 和交互";
        public bool IsEnabled { get; set; } = false;  // 默认关闭
        
        public List<UIProblem> Check(GameObject root)
        {
            var problems = new List<UIProblem>();
            
            if (root.GetComponent<CanvasGroup>() == null && root.GetComponent<Canvas>() == null)
            {
                // 检查是否是 UI 面板根节点 (有 RectTransform 且为一级子节点)
                if (root.GetComponent<RectTransform>() != null)
                {
                    problems.Add(new UIProblem
                    {
                        Type = UIProblemType.Warning,
                        RuleName = RuleName,
                        Description = $"'{root.name}' 建议添加 CanvasGroup",
                        Target = root,
                        NodePath = root.name
                    });
                }
            }
            
            return problems;
        }
    }

    #endregion
}
