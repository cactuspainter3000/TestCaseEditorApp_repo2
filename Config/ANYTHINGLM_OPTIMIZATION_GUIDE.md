# AnythingLLM Optimization Settings for Test Case Editor

## Quick Setup During Workspace Creation

### **✅ AUTOMATED - No Manual Configuration Needed!**
The Test Case Editor now **automatically applies all optimization settings** when creating new workspaces:

✅ **Temperature: 0.3** - Automatically configured for consistent JSON responses  
✅ **System Prompt** - Pre-loaded with requirements analysis instructions  
✅ **Context/Response Limits** - Optimized for requirement analysis  
✅ **Training Documents** - Optimization guide automatically uploaded  
✅ **Workspace Naming** - Smart naming for Test Case Editor projects  

**Simply create a new workspace through the Test Case Editor UI and all settings are applied automatically!**

### **Manual Configuration (Only If Needed)**

If you need to create workspaces manually or adjust settings:

### **Automated Setup Checklist**
**✅ DONE AUTOMATICALLY** - The following settings are now applied automatically when creating workspaces through the Test Case Editor:

**✅ During Workspace Creation:**
- [x] Set workspace name: "Test Case Editor Requirements Analysis"
- [x] Select optimal temperature (0.3) for structured responses
- [x] Apply requirements analysis system prompt
- [x] Set context window to 8K+ tokens
- [x] Set max response to 2K tokens

**✅ Immediate Post-Creation:**
- [x] Upload this optimization guide as training document
- [x] Configure optimal settings for JSON output
- [x] Test workspace with sample requirement validation

**Manual Configuration Only If Needed:**
- [ ] Disable source citations in RAG responses (if available)
- [ ] Set similarity threshold to 0.7+ (if available)
- [ ] Configure top K results to 3-5 (if available)  
- [ ] Add embedding model preference (if available)

## Detailed Configuration Options

### 1. **LLM Selection**
- **Best Choice**: Claude 3.5 Sonnet or GPT-4 for structured output
- **Alternative**: Llama 3.1 70B+ for local deployment
- **Avoid**: Smaller models (<7B) for complex JSON analysis

### 2. **Temperature Settings**
```
Temperature: 0.2 - 0.4
```
- Lower temperature = more consistent, structured responses
- Higher temperature = more creative but potentially inconsistent JSON

### 4. **System Prompt (Workspace Level)**
**Copy this exactly into workspace system prompt during creation:**
```
You are a requirements quality analyst. When analyzing requirements:
1. Always respond in valid JSON format when requested
2. Focus on providing specific, actionable improvements 
3. In SuggestedEdit fields, provide actual rewritten text, not instructions
4. Be concise but thorough in your analysis
5. Only suggest improvements based on information available in the requirement
6. Rate quality scores consistently: 1-3=Poor, 4-6=Fair, 7-8=Good, 9-10=Excellent
```

### 5. **Context Window Settings**
- **Context Length**: 8K+ recommended (for full requirement context)
- **Max Response**: 2K tokens (sufficient for detailed JSON analysis)

### 5. **Document Processing**
If you've uploaded training documents to the workspace:
- **Chunk Size**: 1000-1500 tokens
- **Chunk Overlap**: 100-200 tokens  
- **Embedding Model**: text-embedding-3-small or similar

### 6. **RAG Settings** (if available)
- **Top K Results**: 3-5 relevant chunks
- **Similarity Threshold**: 0.7+ for high relevance
- **Include Source Citations**: Disabled (for cleaner JSON output)

### 7. **Response Format Training**
To improve AnythingLLM's JSON response consistency, consider adding these example documents to your workspace:

**Example 1: Good Requirement Analysis**
```json
{
  "QualityScore": 8,
  "Issues": [
    {
      "Category": "Clarity", 
      "Severity": "Low",
      "Description": "Minor terminology could be more specific"
    }
  ],
  "Recommendations": [
    {
      "Category": "Clarity",
      "Description": "Replace vague terms with specific values",
      "SuggestedEdit": "The system shall process user requests within 2 seconds under normal operating conditions"
    }
  ],
  "FreeformFeedback": "Well-structured requirement with clear acceptance criteria",
  "HallucinationCheck": "NO_FABRICATION"
}
```

### 8. **Troubleshooting**

**If responses are too instructional:**
- Add to workspace: "Provide actual text rewrites, not improvement instructions"
- Lower temperature to 0.2
- Add example of good vs bad SuggestedEdit formats

**If JSON format is inconsistent:**
- Add multiple JSON examples to workspace documents
- Use Claude 3.5 Sonnet (best for structured output)
- Add validation examples showing required fields

**If analysis is too generic:**
- Upload domain-specific requirement examples 
- Add context about your verification methods
- Include examples of good requirements in your domain

### 9. **Performance Optimization**
- **Response Speed**: Use smaller, specialized models for JSON tasks
- **Quality vs Speed**: Balance between model size and response time
- **Batch Processing**: Process similar requirements together for consistency

### 10. **Workspace Document Recommendations**
Upload these types of documents for better analysis:
- High-quality requirement examples from your domain
- Requirements writing guidelines/standards
- Common requirement quality issues and fixes
- Verification method definitions and examples

These settings should significantly improve the quality and consistency of requirement analysis while maintaining the faster response times you're now seeing.