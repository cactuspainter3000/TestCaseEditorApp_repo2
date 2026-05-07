# System Analysis: Expected vs Actual Behavior

## Executive Summary

This document compares what the Test Case Editor App requirement extraction system **should be doing** (based on architectural intent) versus what it **actually does** (based on code analysis).

---

## 1. DOCUMENT PROCESSING PIPELINE

### **EXPECTED BEHAVIOR:**
```
1. Document Upload → RAG Indexing
2. RAG Context Retrieval → Contextual Analysis  
3. Quality-Guided Derivation → Filtered Requirements
4. Taxonomy Classification → Categorized Results
5. Validation & Scoring → Final Output
```

### **ACTUAL BEHAVIOR:**
```
1. Document Upload → RAG Indexing ✅
2. Basic Extraction (uses RAG context) → 1 quality requirement ✅
3. ATP Derivation (bypasses RAG) → 546 generic requirements ❌
4. Post-hoc Quality Scoring → Scores garbage ❌  
5. Taxonomy Applied to Results → Categories garbage ❌
```

**GAP:** Two parallel extraction pipelines with different context access.

---

## 2. RAG INTEGRATION

### **EXPECTED BEHAVIOR:**
```
For each requirement analysis step:
1. Query RAG with step-specific keywords
2. Retrieve relevant document chunks  
3. Combine step + context in LLM prompt
4. Generate contextually-grounded requirements
```

### **ACTUAL BEHAVIOR:**
```
Basic Extraction:
✅ Queries RAG with generic keywords
✅ Gets 19,740 chars of context
✅ Generates 1 quality requirement

ATP Derivation:  
❌ Completely bypasses RAG system
❌ Analyzes raw document chunks in isolation
❌ Generates 546 context-free requirements
```

**GAP:** ATP derivation system architecturally unaware of RAG capabilities.

---

## 3. QUALITY SCORING INTEGRATION

### **EXPECTED BEHAVIOR:**
```
During derivation process:
1. Generate candidate requirement
2. Score quality in real-time  
3. Filter out low-quality candidates
4. Only keep high-scoring requirements
```

### **ACTUAL BEHAVIOR:**
```
After derivation complete:
1. Generate all 546 requirements (including garbage)
2. Score everything post-facto
3. Present scored garbage to users
4. No filtering during generation
```

**GAP:** Quality scoring happens too late to prevent garbage generation.

---

## 4. ATP STEP ANALYSIS

### **EXPECTED BEHAVIOR:**
```
For each ATP step:
1. Extract step text + surrounding context
2. Query RAG for domain-specific information
3. Analyze: Step + Context + Domain Knowledge  
4. Generate specific, grounded requirements
```

### **ACTUAL BEHAVIOR:**
```
For each ATP step:
1. Extract step text only
2. No RAG query per step
3. Analyze: Isolated step fragment
4. Generate generic, hallucinated requirements
```

**GAP:** No per-step context enrichment from RAG.

---

## 5. TAXONOMY CLASSIFICATION

### **EXPECTED BEHAVIOR:**
```
During requirement generation:
1. Consider taxonomy categories for derivation guidance
2. Generate requirements aligned with A-N taxonomy
3. Ensure proper category distribution  
4. Validate category appropriateness
```

### **ACTUAL BEHAVIOR:**
```
After requirement generation:
1. Generate requirements without taxonomy awareness
2. Apply A-N categories post-hoc
3. Accept whatever distribution results
4. No category validation during generation
```

**GAP:** Taxonomy used for labeling instead of guiding generation.

---

## 6. CONTEXT PRESERVATION

### **EXPECTED BEHAVIOR:**
```
Document Context Flow:
RAG Indexing → Context Chunks → Analysis Context → Derived Requirements
         ↓            ↓              ↓              ↓
    Full Document   Relevant      Enhanced       Specific
      Content       Snippets      Analysis      Requirements
```

### **ACTUAL BEHAVIOR:**
```
Basic Extraction:
RAG → Context → Analysis ✅

ATP Derivation:  
Raw Document → Fragments → Isolated Analysis ❌
```

**GAP:** Context lost during ATP processing.

---

## 7. SERVICE ORCHESTRATION

### **EXPECTED BEHAVIOR:**
```
Integrated Service Pipeline:
DirectRagService ←→ SystemCapabilityDerivationService ←→ QualityScoringIntegrationService
     ↓                           ↓                              ↓
Document Context    →    Contextual Analysis    →    Quality-Filtered Results
```

### **ACTUAL BEHAVIOR:**
```
Isolated Services:
DirectRagService: Works independently
SystemCapabilityDerivationService: Ignores RAG  
QualityScoringIntegrationService: Scores everything
```

**GAP:** Services don't integrate effectively.

---

## 8. PERFORMANCE CHARACTERISTICS

### **EXPECTED BEHAVIOR:**
```
- 10-30 high-quality, specific requirements
- Processing time: 2-5 minutes  
- Context-aware analysis
- Minimal hallucination
- Domain-specific results
```

### **ACTUAL BEHAVIOR:**
```
- 1 high-quality + 546 generic requirements
- Processing time: 40+ minutes
- Fragmented analysis
- Massive hallucination  
- Generic, unusable results
```

**GAP:** Volume vs quality optimization misalignment.

---

## 9. ERROR HANDLING & FALLBACKS

### **EXPECTED BEHAVIOR:**
```
Graceful Degradation:
1. RAG available → Full context analysis
2. RAG unavailable → Document-based analysis with warnings
3. Quality threshold → Filter requirements by score
4. Timeout handling → Partial results with status
```

### **ACTUAL BEHAVIOR:**
```
Parallel Processing:
1. RAG available → Basic extraction only
2. ATP always runs → Bypasses RAG regardless  
3. No quality filtering → Accept all generated content
4. Timeout recovery → Continue with degraded analysis
```

**GAP:** No intelligent fallback strategy.

---

## 10. USER EXPERIENCE

### **EXPECTED BEHAVIOR:**
```
User sees:
- "Analyzing document with AI context..."
- "Found 15 specific system requirements"
- High-quality, actionable requirements
- Clear derivation rationale per requirement
- Domain-appropriate categorization
```

### **ACTUAL BEHAVIOR:**
```
User sees:  
- "Found 547 requirements: 1 extracted + 546 derived"
- Mix of 1 quality + 546 generic requirements
- Unclear why so many generic requirements
- Questionable requirement quality
- Time-consuming to filter manually
```

**GAP:** Poor signal-to-noise ratio in results.

---

## KEY ARCHITECTURAL PROBLEMS IDENTIFIED

### **1. Parallel Pipeline Anti-Pattern**
- Two separate extraction systems with different capabilities
- No coordination between pipelines
- Inconsistent context access

### **2. Late-Stage Validation**  
- Quality scoring after generation instead of during
- No real-time filtering of poor requirements
- Wasted processing on garbage generation

### **3. Context Loss Pattern**
- RAG context retrieved but not propagated  
- ATP analysis loses document context
- Services operate in isolation

### **4. Optional Integration Everywhere**
- Services designed as optional rather than integrated
- No enforcement of architectural dependencies  
- Graceful degradation instead of proper integration

### **5. Feature Creep Without Integration**
- Each enhancement added independently
- No redesign of core pipeline  
- Bolt-on architecture instead of integrated design

---

## RECOMMENDED FIXES (Priority Order)

### **Priority 1: RAG-ATP Integration**
- Modify ATP derivation to query RAG per step
- Pass context to each ATP analysis
- Single pipeline instead of parallel processing

### **Priority 2: Real-Time Quality Filtering**  
- Move quality scoring into derivation loop
- Filter requirements during generation
- Stop processing low-quality branches early

### **Priority 3: Context Propagation**
- Ensure document context flows through entire pipeline
- Maintain context state across service calls
- Add context validation at each stage

### **Priority 4: Service Integration Architecture**
- Make RAG a required dependency for ATP derivation  
- Design services to work together by default
- Remove optional integration patterns

### **Priority 5: User Experience Optimization**
- Focus on 10-30 quality requirements vs 500+ generic ones
- Provide clear derivation rationale per requirement  
- Implement progressive disclosure of results

---

This analysis reveals that while individual components work well, the **system integration is fundamentally broken**, leading to poor user outcomes despite sophisticated underlying technology.