(() => {
  const status = document.getElementById("bridge-status");
  const content = document.getElementById("app-content");
  const routeNames = { configuration: "Settings", brands: "Brands & templates", books: "Book Library", process: "Interior processing", outputs: "PDF Library", diagnostics: "Diagnostics" };
  const bookStatuses = ["All", "Complete", "Needs review", "Ready", "Processing", "PDF ready", "Failed"];
  const state = { inspectedBrand: "", selectedBookId: "", selectedBookIds: new Set(), selectedBookTab: "settings", bookDrawerOpen: false, bookDrawerScrollTop: 0, artworkGridScrollTop: 0, selectedArtworkReferences: new Set(), assetBulkActive: "unchanged", assetBulkFrameMode: "unchanged", bookInteriorDrafts: new Map(), bookMetadataDrafts: new Map(), bookMetadataValidation: new Map(), keywordBuilderPreviews: new Map(), keywordBuilderRevisions: new Map(), keywordBuilderPending: new Map(), keywordBuilderConfirmed: new Map(), keywordBuilderSubmitted: null, keywordBuilderRefreshPending: false, keywordBuilderRefreshNeeded: false, keywordBuilderRefreshBookId: "", asinResearchDrafts: new Map(), asinResearchSessions: new Map(), asinResearchFeedback: new Map(), asinResearchActiveBookId: "", asinResearchPollTimer: null, amazonBrowserStatus: { state: "Closed", reasonCode: null }, amazonBrowserPending: false, settingsSavePending: false, settingsFeedback: "", settingsFeedbackError: false, storageSnapshot: null, storageLoading: false, storageSettingsPending: false, storagePendingBooks: new Set(), storagePollTimers: new Map(), storageFeedback: "", storageFeedbackError: false, brandAuthorDrafts: new Map(), catalogMutationPending: false, catalogMutationAwaitingSnapshot: false, catalogMutationCommand: "", catalogMutationTarget: "", catalogFeedback: "", catalogFeedbackError: false, introTemplateDimensions: new Map(), introTemplatePage: 1, bookInteriorSavePending: false, bookInteriorSaveTaskId: "", bookInteriorSaveAwaitingSnapshot: false, interiorShufflePending: false, interiorShuffleTaskId: "", interiorShuffleAwaitingSnapshot: false, interiorShuffleFeedback: "", interiorShuffleFeedbackError: false, brandTemplateCopyPending: false, productionImportPending: "", productionActionTaskId: "", productionActionPollTimer: null, productionActionName: "", productionFeedback: "", productionFeedbackError: false, productionFeedbackWarning: false, productionRefreshAwaitingSnapshot: false, productionFocusSelector: "", productionFinalBuildActive: false, bookFilter: "", bookBrandFilter: "All", bookStatus: "All", bookPage: 1, bookSort: "activity", brandFilter: "", brandValidationResult: null, brandValidationRequestBrands: new Map(), selectedAssetReference: "", assetView: "grid", assetStatus: "Active", assetFrameMode: "", pdfLibrarySearch: "", pdfLibrarySort: "newest", pdfLibraryPage: 1, pdfLibraryView: "grid", pdfLibrarySearchFocused: false, pdfLibrarySearchCaret: 0, pdfLibraryFeedback: "", pdfLibraryFeedbackError: false, pdfLibraryPendingActions: new Set(), pdfLibraryRequestActions: new Map(), applicationLoadState: "idle", applicationLoadError: "", libraryRefreshTaskId: "", libraryRefreshPollTimer: null, libraryRefreshResultRequested: false, cacheCleanupTaskId: "", cacheCleanupPollTimer: null, cacheCleanupResultRequested: false, cacheCleanupActive: false, processQueuePage: 1, processQueueScrollTop: 0, processFocusIdentity: null, processStartPending: false, lastTerminalRefreshSession: "", diagnosticsTab: "summary", backgroundTasks: [], pendingCommands: new Map(), updateSnapshot: null, updateCommandPending: "", updatePollTimer: null, updateDismissedVersion: "", updateDialogPreviousFocus: null };

  state.bookKeywordBuilderDrafts = new Map();
  state.bookKeywordBuilderValidation = new Map();
  state.interiorFolderOpenPending = false;
  state.interiorFolderOpenBookId = "";
  state.storageRequestBooks = new Map();
  state.productionPdfNames = new Map();
  state.productionPdfNamePending = new Set();
  state.productionPdfNameErrors = new Map();
  state.productionPdfNameRequests = new Map();
  state.brandCloneOpen = false;
  state.brandCloneLanguageCode = "";
  state.brandClonePending = false;
  state.brandCloneAwaitingSnapshot = false;
  state.brandCloneDestination = "";
  state.brandCloneFeedback = "";
  state.brandCloneFeedbackError = false;
  state.brandCloneNotice = "";
  state.bookCloneOpen = false;
  state.bookCloneSourceId = "";
  state.bookCloneLanguageCode = "";
  state.bookClonePending = false;
  state.bookCloneAwaitingSnapshot = false;
  state.bookCloneDestinationId = "";
  state.bookCloneDestination = "";
  state.bookCloneFeedback = "";
  state.bookCloneFeedbackError = false;
  state.bookCloneNotice = "";
  state.bookCloneNoticeBookId = "";
  state.settingsGenericLanguageCode = "en";
  state.settingsGenericKeywordDrafts = new Map();
  state.settingsGenericKeywordDraftsInitialized = false;
  state.settingsAmazonLanguageCode = "en";
  state.settingsAmazonProfileDrafts = new Map();
  state.settingsAmazonProfileDraftsInitialized = false;

  const escapeHtml = (value) => String(value ?? "").replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", "\"": "&quot;" }[character]));
  const valueFor = (object, name, fallback = null) => object?.[name] ?? object?.[name[0].toUpperCase() + name.slice(1)] ?? fallback;
  const discovery = () => valueFor(window.appSnapshot, "discovery", {});
  const books = () => valueFor(discovery(), "books", []);
  const brands = () => valueFor(discovery(), "brands", []);
  const summaries = () => valueFor(window.appSnapshot, "bookSummaries", []);
  const bookId = (book) => valueFor(valueFor(book, "id", {}), "value", valueFor(book, "name", ""));
  const summaryFor = (book) => summaries().find((summary) => valueFor(valueFor(summary, "bookId", {}), "value", "") === bookId(book));
  const pdfLibraryBookName = (book, summary) => bookId(book) || valueFor(valueFor(summary, "bookId", {}), "value", "");
  const pdfLibraryPageSize = 12;
  const processQueuePageSize = 12;
  const pdfLibraryOutputs = (summary) => {
    const newestByKind = new Map();
    for (const output of valueFor(summary, "outputSummaries", [])) {
      const kind = String(valueFor(output, "artifactKind", "Unknown"));
      if (kind !== "Cover" && kind !== "Interior") continue;
      const existing = newestByKind.get(kind);
      const generatedAt = new Date(valueFor(output, "generatedAt", 0)).getTime() || 0;
      const existingGeneratedAt = existing ? new Date(valueFor(existing, "generatedAt", 0)).getTime() || 0 : -1;
      const artifact = String(valueFor(output, "artifactReference", ""));
      const existingArtifact = String(valueFor(existing, "artifactReference", ""));
      if (!existing || generatedAt > existingGeneratedAt || (generatedAt === existingGeneratedAt && artifact.localeCompare(existingArtifact) > 0)) newestByKind.set(kind, output);
    }
    return ["Cover", "Interior"].map((kind) => newestByKind.get(kind)).filter(Boolean);
  };
  const pdfLibraryOutputSize = (summary) => pdfLibraryOutputs(summary).reduce((total, output) => total + (Number(valueFor(output, "fileSizeBytes", 0)) || 0), 0);
  const pdfLibraryGeneratedAt = (summary) => {
    const outputTimes = pdfLibraryOutputs(summary).map((output) => new Date(valueFor(output, "generatedAt", 0)).getTime()).filter((value) => Number.isFinite(value) && value > 0);
    if (outputTimes.length) return Math.max(...outputTimes);
    const lastRun = new Date(valueFor(summary, "lastRunAt", 0)).getTime();
    return Number.isFinite(lastRun) ? lastRun : 0;
  };
  const eligiblePdfLibraryBooks = () => books().map((book) => ({ book, summary: summaryFor(book) })).filter(({ summary }) => summary && pdfLibraryOutputs(summary).length > 0);
  const pdfLibraryBooks = () => {
    const items = eligiblePdfLibraryBooks();
    const search = state.pdfLibrarySearch.trim().toLocaleLowerCase();
    const filtered = search ? items.filter(({ book, summary }) => pdfLibraryBookName(book, summary).toLocaleLowerCase().includes(search)) : items;
    return [...filtered].sort((left, right) => {
      if (state.pdfLibrarySort === "name") return pdfLibraryBookName(left.book, left.summary).localeCompare(pdfLibraryBookName(right.book, right.summary), undefined, { sensitivity: "base" });
      if (state.pdfLibrarySort === "size") return pdfLibraryOutputSize(right.summary) - pdfLibraryOutputSize(left.summary);
      return pdfLibraryGeneratedAt(right.summary) - pdfLibraryGeneratedAt(left.summary);
    });
  };
  const displayStatus = (value) => typeof value === "number" ? ["Not started", "Running", "Failed", "Cancelled", "Completed", "Interrupted"][value] ?? "Unknown" : value;
  const brandValidationStatus = (value) => typeof value === "number" ? ["Not validated", "Validated", "Needs validation"][value] ?? "Not validated" : String(value ?? "NotValidated").replace(/([a-z])([A-Z])/g, "$1 $2");
  const brandSummaries = () => valueFor(window.appSnapshot, "brandSummaries", []);
  const supportedLanguages = () => valueFor(window.appSnapshot, "supportedLanguages", []);
  const brandSummaryFor = (brand) => brandSummaries().find((summary) => valueFor(summary, "brandName", "") === valueFor(brand, "name", ""));
  const languageEditionDestinationName = (sourceName, languageCode) => {
    const language = supportedLanguages().find((option) => String(valueFor(option, "code", "")).toLocaleLowerCase() === String(languageCode ?? "").trim().toLocaleLowerCase());
    if (!sourceName || !language) return "";
    let baseName = String(sourceName);
    for (const option of supportedLanguages()) {
      const suffix = `_${String(valueFor(option, "code", "")).toLocaleLowerCase()}`;
      if (!baseName.toLocaleLowerCase().endsWith(suffix)) continue;
      baseName = baseName.slice(0, -suffix.length);
      break;
    }
    return `${baseName}_${String(valueFor(language, "code", "")).toLocaleLowerCase()}`;
  };
  const brandCloneDestinationName = (sourceBrandName, languageCode) => languageEditionDestinationName(sourceBrandName, languageCode);
  const bookCloneDestinationName = (sourceBookName, languageCode) => languageEditionDestinationName(sourceBookName, languageCode);
  const resetBrandClone = (clearNotice = true) => {
    state.brandCloneOpen = false;
    state.brandCloneLanguageCode = "";
    state.brandClonePending = false;
    state.brandCloneAwaitingSnapshot = false;
    state.brandCloneDestination = "";
    state.brandCloneFeedback = "";
    state.brandCloneFeedbackError = false;
    if (clearNotice) state.brandCloneNotice = "";
  };
  const brandCloneErrorMessage = (error) => ({
    invalid_brand_clone: "Review the Brand and language, then try again.",
    brand_clone_destination_exists: "That destination Brand already exists. Select another language.",
    brand_clone_source_not_found: "The source Brand no longer exists. Refresh the library and try again.",
    brand_clone_language_invalid: "Select a supported language.",
    processing_active: "Wait for Interior processing to finish.",
    snapshot_unavailable: "The Brand library is unavailable. Refresh it and try again.",
    brand_clone_failed: "The Brand could not be cloned. The source Brand was not changed."
  })[String(error)] ?? "The Brand could not be cloned. The source Brand was not changed.";
  const resetBookClone = (clearNotice = true) => {
    state.bookCloneOpen = false;
    state.bookCloneSourceId = "";
    state.bookCloneLanguageCode = "";
    state.bookClonePending = false;
    state.bookCloneAwaitingSnapshot = false;
    state.bookCloneDestinationId = "";
    state.bookCloneDestination = "";
    state.bookCloneFeedback = "";
    state.bookCloneFeedbackError = false;
    if (clearNotice) {
      state.bookCloneNotice = "";
      state.bookCloneNoticeBookId = "";
    }
  };
  const bookCloneErrorMessage = (error) => ({
    invalid_book_clone: "Review the Book and language, then try again.",
    book_clone_destination_exists: "That destination Book already exists. Select another language.",
    book_clone_source_not_found: "The source Book no longer exists. Refresh the library and try again.",
    book_clone_language_invalid: "Select a supported language.",
    book_clone_state_invalid: "The source Book state or saved references are invalid. Repair them before cloning.",
    processing_active: "Wait for Interior processing to finish.",
    production_action_active: "Wait for the active Production action to finish.",
    cache_cleanup_active: "Wait for Clear Cache to finish.",
    snapshot_unavailable: "The Book library is unavailable. Refresh it and try again.",
    book_clone_failed: "The Book could not be cloned. The source Book was not changed."
  })[String(error)] ?? "The Book could not be cloned. The source Book was not changed.";
  const assignmentStatus = (summary) => {
    const value = valueFor(summary, "assignmentStatus", "Unassigned");
    return typeof value === "number" ? ["Unassigned", "Valid", "BookAuthorMissing", "BrandAuthorMissing", "AuthorMismatch", "MissingBrand", "BrandMetadataUnavailable", "LanguageMismatch"][value] ?? "Unassigned" : String(value ?? "Unassigned");
  };
  const assignmentLabel = (summary) => ({ BookAuthorMissing: "Book Author missing", BrandAuthorMissing: "Brand Author missing", AuthorMismatch: "Author mismatch", MissingBrand: "Brand missing", BrandMetadataUnavailable: "Brand metadata unavailable", LanguageMismatch: "Language mismatch" })[assignmentStatus(summary)] ?? assignmentStatus(summary);
  const languageCodeFor = (summary) => String(valueFor(summary, "languageCode", "en") || "en").toLocaleLowerCase();
  const languageNameFor = (summary) => String(valueFor(summary, "languageName", "English") || "English");
  const metadataFor = (summary) => valueFor(summary, "metadata", {}) ?? {};
  const bookDisplayTitle = (book, summary = summaryFor(book)) => String(valueFor(metadataFor(summary), "title", "") || valueFor(book, "name", bookId(book)) || "Unknown");
  const normalizedAuthor = (value) => String(value ?? "").trim().toLowerCase();
  const authorMatches = (left, right) => Boolean(normalizedAuthor(left)) && normalizedAuthor(left) === normalizedAuthor(right);
  const assignedBrandName = (summary) => String(valueFor(summary, "assignedBrand", "") ?? "").trim();
  const assignedBrandFor = (summary) => {
    const assigned = assignedBrandName(summary);
    return assigned ? brands().find((brand) => valueFor(brand, "name", "") === assigned) ?? null : null;
  };
  const brandAuthor = (brand) => String(valueFor(brandSummaryFor(brand), "author", "") ?? "");
  const languageBrandsFor = (summary) => {
    const languageCode = languageCodeFor(summary);
    return brands().filter((brand) => languageCodeFor(brandSummaryFor(brand)) === languageCode);
  };
  const matchingBrandsFor = (summary) => {
    const author = valueFor(metadataFor(summary), "author", "");
    return languageBrandsFor(summary).filter((brand) => authorMatches(author, brandAuthor(brand)));
  };
  const metadataValues = (summary) => {
    const metadata = metadataFor(summary);
    return { title: String(valueFor(metadata, "title", "") ?? "").toUpperCase(), subtitle: String(valueFor(metadata, "subtitle", "") ?? ""), subcover: String(valueFor(metadata, "subcover", "") ?? ""), asin: String(valueFor(metadata, "asin", "") ?? ""), description: String(valueFor(metadata, "description", "") ?? ""), author: String(valueFor(metadata, "author", "") ?? "") };
  };
  const metadataDraftFor = (book, summary, create = false) => {
    const id = bookId(book);
    let draft = state.bookMetadataDrafts.get(id);
    if (!draft && create) { draft = { ...metadataValues(summary) }; state.bookMetadataDrafts.set(id, draft); }
    return draft ?? metadataValues(summary);
  };
  const hasMetadataDraft = (book, summary) => {
    const draft = state.bookMetadataDrafts.get(bookId(book));
    if (!draft) return false;
    const persisted = metadataValues(summary);
    return Object.keys(persisted).some((key) => draft[key] !== persisted[key]);
  };
  const metadataFieldOrder = ["title", "subtitle", "subcover", "asin", "author"];
  const metadataTermSeparator = /[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+/u;
  const metadataEffectiveValue = (value) => String(value ?? "").trim();
  const metadataTerms = (value) => { const effective = metadataEffectiveValue(value); return effective ? effective.split(metadataTermSeparator).filter(Boolean) : []; };
  const metadataGraphemeCount = (value) => {
    const effective = metadataEffectiveValue(value);
    if (!effective) return 0;
    return typeof Intl?.Segmenter === "function" ? [...new Intl.Segmenter(undefined, { granularity: "grapheme" }).segment(effective)].length : null;
  };
  const asciiFold = (value) => /^[\x00-\x7f]+$/.test(value) ? value.toLowerCase() : null;
  const metadataTermsDuplicate = (left, right) => {
    if (left === right) return true;
    const foldedLeft = asciiFold(left);
    const foldedRight = asciiFold(right);
    if (foldedLeft === null || foldedRight === null) return false;
    if (foldedLeft === foldedRight) return true;
    const longer = foldedLeft.length > foldedRight.length ? foldedLeft : foldedRight;
    const shorter = foldedLeft.length > foldedRight.length ? foldedRight : foldedLeft;
    return longer.length === shorter.length + 1 && longer.endsWith("s") && longer.slice(0, -1) === shorter;
  };
  const duplicateMetadataErrors = (field, label, value) => {
    const terms = metadataTerms(value);
    const emitted = [];
    const errors = [];
    for (let leftIndex = 0; leftIndex < terms.length; leftIndex += 1) {
      for (let rightIndex = leftIndex + 1; rightIndex < terms.length; rightIndex += 1) {
        const left = terms[leftIndex];
        const right = terms[rightIndex];
        const relationshipExists = emitted.some(([existingLeft, existingRight]) => {
          const first = asciiFold(existingLeft) ?? existingLeft;
          const second = asciiFold(existingRight) ?? existingRight;
          const candidateLeft = asciiFold(left) ?? left;
          const candidateRight = asciiFold(right) ?? right;
          return first === candidateLeft && second === candidateRight || first === candidateRight && second === candidateLeft;
        });
        if (!metadataTermsDuplicate(left, right) || relationshipExists) continue;
        emitted.push([left, right]);
        errors.push({ field, code: "duplicate_terms", message: `${label} contains duplicate terms: "${left}" and "${right}".`, tokens: [left, right] });
      }
    }
    return errors;
  };
  const validateBookMetadataDraft = (draft, onlyField = "") => {
    const errors = [];
    const include = (field) => !onlyField || onlyField === field;
    const singleLine = (field, label) => {
      const effective = metadataEffectiveValue(draft?.[field]);
      if (effective && /[\r\n]/.test(effective)) errors.push({ field, code: "single_line", message: `${label} must be a single line.` });
    };
    const characterLimit = (field, label, maximumExclusive) => {
      const count = metadataGraphemeCount(draft?.[field]);
      if (count !== null && count >= maximumExclusive) errors.push({ field, code: "character_limit", message: `${label} must be under ${maximumExclusive} characters (currently ${count}).` });
    };
    const maximumCharacterLimit = (field, label, maximumInclusive) => {
      const count = metadataGraphemeCount(draft?.[field]);
      if (count !== null && count > maximumInclusive) errors.push({ field, code: "character_limit", message: `${label} must contain at most ${maximumInclusive} characters (currently ${count}).` });
    };
    if (include("title")) {
      singleLine("title", "Title");
      const terms = metadataTerms(draft?.title);
      if (terms.length && (terms.length < 2 || terms.length > 3)) errors.push({ field: "title", code: "term_count", message: `Title must contain 2 or 3 terms (currently ${terms.length}).` });
      characterLimit("title", "Title", 120);
      errors.push(...duplicateMetadataErrors("title", "Title", draft?.title));
    }
    if (include("subtitle")) {
      singleLine("subtitle", "Subtitle");
      characterLimit("subtitle", "Subtitle", 120);
      errors.push(...duplicateMetadataErrors("subtitle", "Subtitle", draft?.subtitle));
    }
    if (include("subcover")) {
      singleLine("subcover", "Subcover");
      const terms = metadataTerms(draft?.subcover);
      if (terms.length && (terms.length < 4 || terms.length > 6)) errors.push({ field: "subcover", code: "term_count", message: `Subcover must contain 4 to 6 terms (currently ${terms.length}).` });
      characterLimit("subcover", "Subcover", 100);
    }
    if (include("asin")) {
      singleLine("asin", "ASIN");
      maximumCharacterLimit("asin", "ASIN", 100);
    }
    if (include("author")) singleLine("author", "Author");
    return errors;
  };
  const metadataValidationFor = (id) => state.bookMetadataValidation.get(id) ?? { attempted: false, errors: [] };
  const metadataValidationSummary = (errors) => {
    const fields = metadataFieldOrder.filter((field) => errors.some((error) => error.field === field));
    const first = fields[0] ? fields[0][0].toUpperCase() + fields[0].slice(1) : "Book Information";
    return `Book Information was not saved. Fix ${errors.length} issue${errors.length === 1 ? "" : "s"} in ${fields.length} field${fields.length === 1 ? "" : "s"}, starting with ${first}.`;
  };
  const keywordBuilderFor = (summary) => valueFor(summary, "keywordBuilder", null);
  const normalizeKeywordPhrases = (sourceText) => String(sourceText ?? "").split(/\r?\n/u).map((line) => metadataTerms(line).join(" ")).filter(Boolean);
  const persistedGenericKeywords = (languageCode) => {
    const settings = valueFor(window.appSnapshot, "globalSettings", {});
    const profiles = valueFor(settings, "genericKeywordsByLanguage", null);
    const code = String(languageCode ?? "en").toLocaleLowerCase();
    if (profiles && Array.isArray(valueFor(profiles, code, null))) return valueFor(profiles, code, []).map(String);
    return code === "en" ? valueFor(settings, "genericKeywords", []).map(String) : [];
  };
  const resetGenericKeywordDrafts = () => {
    const languages = supportedLanguages();
    state.settingsGenericKeywordDrafts = new Map(languages.map((language) => {
      const code = String(valueFor(language, "code", "")).toLocaleLowerCase();
      return [code, persistedGenericKeywords(code).join("\n")];
    }));
    if (!state.settingsGenericKeywordDrafts.has(state.settingsGenericLanguageCode)) {
      state.settingsGenericLanguageCode = state.settingsGenericKeywordDrafts.has("en") ? "en" : state.settingsGenericKeywordDrafts.keys().next().value ?? "";
    }
    state.settingsGenericKeywordDraftsInitialized = true;
  };
  const ensureGenericKeywordDrafts = () => {
    if (!state.settingsGenericKeywordDraftsInitialized) resetGenericKeywordDrafts();
  };
  const storeGenericKeywordEditorDraft = () => {
    const editor = document.querySelector("[data-generic-keywords]");
    if (editor && state.settingsGenericLanguageCode) state.settingsGenericKeywordDrafts.set(state.settingsGenericLanguageCode, String(editor.value ?? ""));
  };
  const showGenericKeywordLanguage = (languageCode) => {
    storeGenericKeywordEditorDraft();
    const language = supportedLanguages().find((option) => String(valueFor(option, "code", "")).toLocaleLowerCase() === String(languageCode ?? "").toLocaleLowerCase());
    if (!language) return;
    state.settingsGenericLanguageCode = String(valueFor(language, "code", "")).toLocaleLowerCase();
    const editor = document.querySelector("[data-generic-keywords]");
    if (editor) editor.value = state.settingsGenericKeywordDrafts.get(state.settingsGenericLanguageCode) ?? "";
    const label = document.querySelector("[data-generic-keyword-language-label]");
    if (label) label.textContent = `${valueFor(language, "name", state.settingsGenericLanguageCode)} (${state.settingsGenericLanguageCode})`;
  };
  const persistedAmazonMarketplaceProfile = (languageCode) => {
    const profiles = valueFor(valueFor(window.appSnapshot, "globalSettings", {}), "amazonMarketplaceProfiles", {});
    return valueFor(profiles, String(languageCode ?? "en").toLocaleLowerCase(), {}) ?? {};
  };
  const resetAmazonMarketplaceDrafts = () => {
    state.settingsAmazonProfileDrafts = new Map(supportedLanguages().map((language) => {
      const code = String(valueFor(language, "code", "")).toLocaleLowerCase();
      const profile = persistedAmazonMarketplaceProfile(code);
      return [code, {
        profileKey: String(valueFor(profile, "profileKey", "") ?? ""),
        baseUrl: String(valueFor(profile, "baseUrl", "") ?? ""),
        locale: String(valueFor(profile, "locale", "") ?? ""),
        titleTerms: String(valueFor(profile, "titleTerms", "") ?? "")
      }];
    }));
    if (!state.settingsAmazonProfileDrafts.has(state.settingsAmazonLanguageCode)) {
      state.settingsAmazonLanguageCode = state.settingsAmazonProfileDrafts.has("en") ? "en" : state.settingsAmazonProfileDrafts.keys().next().value ?? "";
    }
    state.settingsAmazonProfileDraftsInitialized = true;
  };
  const ensureAmazonMarketplaceDrafts = () => {
    if (!state.settingsAmazonProfileDraftsInitialized) resetAmazonMarketplaceDrafts();
  };
  const storeAmazonMarketplaceEditorDraft = () => {
    if (!state.settingsAmazonLanguageCode) return;
    const draft = { ...(state.settingsAmazonProfileDrafts.get(state.settingsAmazonLanguageCode) ?? {}) };
    document.querySelectorAll("[data-amazon-profile-field]").forEach((input) => { draft[input.dataset.amazonProfileField] = String(input.value ?? ""); });
    state.settingsAmazonProfileDrafts.set(state.settingsAmazonLanguageCode, draft);
  };
  const showAmazonMarketplaceLanguage = (languageCode) => {
    storeAmazonMarketplaceEditorDraft();
    const language = supportedLanguages().find((option) => String(valueFor(option, "code", "")).toLocaleLowerCase() === String(languageCode ?? "").toLocaleLowerCase());
    if (!language) return;
    state.settingsAmazonLanguageCode = String(valueFor(language, "code", "")).toLocaleLowerCase();
    const draft = state.settingsAmazonProfileDrafts.get(state.settingsAmazonLanguageCode) ?? {};
    document.querySelectorAll("[data-amazon-profile-field]").forEach((input) => { input.value = String(valueFor(draft, input.dataset.amazonProfileField, "") ?? ""); });
    const label = document.querySelector("[data-amazon-market-language-label]");
    if (label) label.textContent = `${valueFor(language, "name", state.settingsAmazonLanguageCode)} (${state.settingsAmazonLanguageCode})`;
  };
  const normalizeKeywordBuilderDraft = (draft) => ({ keywords: normalizeKeywordPhrases(draft?.sourceText), adsAsin: String(draft?.adsAsin ?? "").trim() });
  const keywordBuilderPersistedValues = (summary) => {
    const saved = keywordBuilderFor(summary);
    return {
      keywords: valueFor(saved, "sourceKeywords", []).map(String),
      adsAsin: String(valueFor(saved, "adsAsin", "") ?? "")
    };
  };
  const keywordBuilderDraftFor = (book, summary, create = false) => {
    const id = bookId(book);
    let draft = state.bookKeywordBuilderDrafts.get(id);
    if (!draft && create) {
      const persisted = keywordBuilderPersistedValues(summary);
      draft = { sourceText: persisted.keywords.join("\n"), adsAsin: persisted.adsAsin };
      state.bookKeywordBuilderDrafts.set(id, draft);
    }
    if (draft) return draft;
    const persisted = keywordBuilderPersistedValues(summary);
    return { sourceText: persisted.keywords.join("\n"), adsAsin: persisted.adsAsin };
  };
  const keywordBuilderDraftMatches = (left, right) => JSON.stringify(normalizeKeywordBuilderDraft(left)) === JSON.stringify(normalizeKeywordBuilderDraft(right));
  const hasKeywordBuilderDraft = (book, summary) => {
    const draft = state.bookKeywordBuilderDrafts.get(bookId(book));
    if (!draft) return false;
    const persisted = keywordBuilderPersistedValues(summary);
    return JSON.stringify(normalizeKeywordBuilderDraft(draft)) !== JSON.stringify(persisted);
  };
  const setSummaryKeywordBuilder = (id, builder) => {
    const summary = summaries().find((item) => valueFor(valueFor(item, "bookId", {}), "value", "") === id);
    if (!summary) return;
    if (Object.hasOwn(summary, "KeywordBuilder") && !Object.hasOwn(summary, "keywordBuilder")) summary.KeywordBuilder = builder;
    else summary.keywordBuilder = builder;
  };
  const keywordRevisionFor = (id) => state.keywordBuilderRevisions.get(id) ?? 0;
  const invalidateKeywordPreview = (id) => {
    state.keywordBuilderRevisions.set(id, keywordRevisionFor(id) + 1);
    state.keywordBuilderPreviews.delete(id);
  };
  const keywordPreviewFor = (id) => state.keywordBuilderPreviews.get(id) ?? null;
  const keywordOutputFor = (id, summary) => valueFor(keywordPreviewFor(id), "preview", null) ?? keywordBuilderFor(summary);
  const keywordPendingFor = (id) => state.keywordBuilderPending.get(id) ?? "";
  const keywordBuilderCopyText = (builder) => {
    const fields = Array.from({ length: 7 }, (_, index) => String(valueFor(builder, `keyword_${index + 1}`, "") ?? ""));
    fields.push(String(valueFor(builder, "adsKeyword", "") ?? ""));
    fields.push(String(valueFor(builder, "adsAsin", "") ?? ""));
    return fields.join("\t");
  };
  const browserStateName = (value) => typeof value === "number" ? ["Closed", "Checking", "Downloading", "Opening", "WarmingUp", "Ready", "NeedsAttention", "Error"][value] ?? "Error" : String(value ?? "Closed");
  const asinOutcomeName = (value) => typeof value === "number" ? ["Idle", "Running", "Completed", "Partial", "NeedsAttention", "Failed", "Cancelled"][value] ?? "Idle" : String(value ?? "Idle");
  const asinRowStatusName = (value) => typeof value === "number" ? ["Pending", "Searching", "Selected", "NoSearchResult", "NoMatchingTitle", "AllCandidatesUsed", "Failed", "Cancelled", "NotProcessed"][value] ?? "Pending" : String(value ?? "Pending");
  const normalizedAsinSearchKeywords = (sourceText) => {
    const seen = new Set();
    const phrases = String(sourceText ?? "")
      .split(",")
      .flatMap((value) => value.split(/\r?\n/u))
      .map((value) => metadataTerms(value).join(" "))
      .filter(Boolean);
    return phrases.filter((keyword) => {
      const key = keyword.toLocaleLowerCase();
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  };
  const asinResearchDraftFor = (book, summary) => {
    const id = bookId(book);
    let draft = state.asinResearchDrafts.get(id);
    if (!draft) {
      draft = { autoApplyPending: false, baseReceipt: "", targetRevision: -1, appliedRevision: -1 };
      state.asinResearchDrafts.set(id, draft);
    }
    return draft;
  };
  const asinSessionFor = (id) => state.asinResearchSessions.get(id) ?? null;
  const asinSessionView = (session) => valueFor(session, "view", null);
  const asinSessionActive = (session) => Boolean(valueFor(session, "isActive", false) || valueFor(session, "isCancelling", false));
  const asinFinalValue = (session) => String(valueFor(asinSessionView(session), "finalAsins", "") ?? "");
  const asinResultIsStale = (id) => {
    const draft = state.asinResearchDrafts.get(id);
    const sessionDigest = String(valueFor(asinSessionFor(id), "receiptDigest", "") ?? "");
    const previewDigest = String(valueFor(keywordPreviewFor(id), "receiptDigest", "") ?? "");
    const currentRevision = keywordRevisionFor(id);
    const appliedToCurrentDraft = draft?.appliedRevision === currentRevision;
    return !draft
      || !previewDigest
      || !appliedToCurrentDraft && draft.targetRevision !== currentRevision
      || !appliedToCurrentDraft && Boolean(sessionDigest && sessionDigest !== previewDigest);
  };
  const asinReasonLabel = (code) => ({
    amazon_no_search_result: "No search results",
    amazon_no_matching_title: "No title matching this market’s book terms",
    amazon_all_candidates_used: "Matching ASIN already used",
    amazon_not_processed: "Not searched — crawl stopped",
    amazon_crawl_cancelled: "Search cancelled",
    amazon_fetch_timeout: "Search timed out",
    amazon_fetch_failed: "Search failed",
    amazon_fetch_payload_invalid: "Browser response could not be parsed",
    amazon_challenge_detected: "Amazon needs attention in the browser",
    amazon_rate_limited: "Amazon rate limited the search",
    amazon_searchbox_missing: "Amazon search box is missing from the response",
    amazon_markup_unsupported: "Amazon result layout is not supported"
  })[String(code ?? "")] ?? "Search failed";
  const asinFeedbackFor = (id) => state.asinResearchFeedback.get(id) ?? { message: "", error: false };
  const setAsinFeedback = (id, message, error = false) => state.asinResearchFeedback.set(id, { message, error });
  const pollAmazonAsinCrawl = () => {
    if (state.asinResearchActiveBookId) send("book.keywords.asin-crawl.get", { bookId: state.asinResearchActiveBookId });
  };
  const stopAmazonAsinPolling = () => {
    if (state.asinResearchPollTimer !== null && window.clearInterval) window.clearInterval(state.asinResearchPollTimer);
    state.asinResearchPollTimer = null;
  };
  const observeAmazonAsinCrawl = (session) => {
    const id = String(valueFor(session, "bookId", "") ?? "");
    if (!id) return;
    state.asinResearchSessions.set(id, session);
    if (asinSessionActive(session)) {
      state.asinResearchActiveBookId = id;
      if (state.asinResearchPollTimer === null) state.asinResearchPollTimer = window.setInterval(pollAmazonAsinCrawl, 500);
    } else if (state.asinResearchActiveBookId === id) {
      state.asinResearchActiveBookId = "";
      stopAmazonAsinPolling();
      const view = asinSessionView(session);
      state.amazonBrowserStatus = {
        state: "Closed",
        reasonCode: null,
        targetMarketName: valueFor(view, "marketName", valueFor(state.amazonBrowserStatus, "targetMarketName", null)),
        targetDomain: valueFor(view, "marketplaceDomain", valueFor(state.amazonBrowserStatus, "targetDomain", null))
      };
    }
    patchAsinResearch(id);
  };
  const autoStageAsinCrawlResults = (session) => {
    const id = String(valueFor(session, "bookId", "") ?? "");
    const researchDraft = state.asinResearchDrafts.get(id);
    const view = asinSessionView(session);
    const outcome = asinOutcomeName(valueFor(view, "outcome", "Idle"));
    const value = String(valueFor(view, "finalAsins", "") ?? "");
    const count = value.split(",").filter(Boolean).length;
    if (!id || asinSessionActive(session) || outcome === "Idle") return { status: "pending", count };
    const shouldApply = Boolean(researchDraft?.autoApplyPending);
    if (researchDraft) researchDraft.autoApplyPending = false;
    if (!shouldApply) return { status: "skipped", count };
    if (asinResultIsStale(id)) return { status: "stale", count };
    if (!value || !/^[A-Z0-9]{10}(,[A-Z0-9]{10})*$/u.test(value)) return { status: value ? "invalid" : "empty", count };
    const book = books().find((item) => bookId(item) === id);
    const summary = book ? summaryFor(book) : null;
    if (!book || !summary) return { status: "invalid", count };
    const previewState = keywordPreviewFor(id);
    const baseReceipt = String(researchDraft?.baseReceipt || valueFor(previewState, "receipt", "") || "");
    if (!baseReceipt) return { status: "stale", count };
    const keywordDraft = keywordBuilderDraftFor(book, summary, true);
    const merged = [...new Set(`${keywordDraft.adsAsin || ""},${value}`.split(/[\r\n,]+/u).map((item) => item.trim().toUpperCase()).filter(Boolean))].join(",");
    if (keywordDraft.adsAsin === merged) return { status: "unchanged", count };
    keywordDraft.adsAsin = merged;
    const clientRevision = keywordRevisionFor(id) + 1;
    state.keywordBuilderRevisions.set(id, clientRevision);
    researchDraft.appliedRevision = clientRevision;
    state.keywordBuilderPending.set(id, "update-ads-asin");
    state.catalogMutationCommand = "book.keywords.preview.update-ads-asin";
    state.catalogMutationTarget = id;
    send("book.keywords.preview.update-ads-asin", { bookId: id, baseReceipt, adsAsin: merged || null, clientRevision });
    state.bookKeywordBuilderValidation.delete(id);
    if (state.catalogMutationTarget === id && state.catalogMutationCommand === "book.keywords.save") {
      state.catalogFeedback = "";
      state.catalogFeedbackError = false;
    }
    if (id === state.selectedBookId) {
      refreshBookKeywordBuilderCard();
      const unsaved = document.querySelector("[data-book-interior-unsaved]");
      if (unsaved) unsaved.hidden = !(hasInteriorDraft(id) || hasMetadataDraft(book, summary) || hasKeywordBuilderDraft(book, summary));
    }
    return { status: "updating", count };
  };
  const brandAuthorDraftFor = (brand) => state.brandAuthorDrafts.get(valueFor(brand, "name", "")) ?? brandAuthor(brand);
  const brandAuthorIsDirty = (brand, value) => value !== brandAuthor(brand) || valueFor(brandSummaryFor(brand), "metadataStatus", "Missing") === "Unavailable";
  const catalogMutationBusy = () => state.catalogMutationPending || state.catalogMutationAwaitingSnapshot;
  const metadataAssignmentWarning = (draft, summary) => {
    const assigned = assignedBrandName(summary);
    if (!assigned) return "";
    const brand = brands().find((item) => valueFor(item, "name", "") === assigned);
    return brand && authorMatches(draft.author, brandAuthor(brand)) ? "" : `Saving this Author will make the '${assigned}' assignment invalid. The assignment will be kept for review.`;
  };
  const isValidatedBrand = (brand) => brandValidationStatus(valueFor(brandSummaryFor(brand), "validationStatus", "NotValidated")) === "Validated";
  const frameModeValue = (value) => {
    if (typeof value === "number") return ["disabled", "enabled"][value] ?? "disabled";
    const normalized = String(value ?? "disabled").toLowerCase();
    return ["enabled", "disabled"].includes(normalized) ? normalized : "disabled";
  };
  const workspaceStatus = (summary) => displayStatus(valueFor(summary, "workspaceStatus", "Not started"));
  const workspaceStateAvailable = (summary) => valueFor(summary, "workspaceStateAvailable", true) !== false;
  const bookIsCompleted = (summary) => valueFor(summary, "isCompleted", false) === true;
  const productionStatus = (summary, book = null) => {
    const workspace = workspaceStatus(summary);
    const validation = valueFor(summary, "validationStatus", "Needs review");
    const outputs = valueFor(summary, "outputSummaries", []);
    if (workspace === "Failed" || validation === "Invalid") return "Failed";
    if (workspace === "Running") return "Processing";
    if (book && !introReadiness(book, summary).ready) return "Needs review";
    if (outputs.some((output) => ["Verified", "Available"].includes(valueFor(output, "verificationStatus", "")))) return "PDF ready";
    return validation === "Ready" ? "Ready" : "Needs review";
  };
  const bookFrameState = (summary) => {
    const modes = valueFor(summary, "interiorSourcePages", []).map((source) => frameModeValue(valueFor(source, "frameMode", "disabled")));
    if (!modes.length) return "Needs review";
    if (modes.every((mode) => mode === "enabled")) return "Frame";
    if (modes.every((mode) => mode === "disabled")) return "No Frame";
    return "Mixed";
  };
  const statusClass = (value) => value === "Ready" || value === "Completed" || value === "Present" || value === "Validated" || value === "Processed" || value === "Production" ? "status-good" : value === "Invalid" || value === "Failed" || value === "Unreadable" || value === "Needs attention" || value === "Missing" || value === "Author mismatch" || value === "Book Author missing" || value === "Brand Author missing" || value === "Brand missing" || value === "Brand metadata unavailable" ? "status-bad" : value === "Needs selection" || value === "Needs validation" || value === "Running" || value === "Processing" || value === "Stale" || value === "Ready to process" ? "status-warn" : "status-muted";
  const badge = (value) => { const label = displayStatus(value); return `<span class="status-badge ${statusClass(label)}">${escapeHtml(label)}</span>`; };
  const bookSelectIcon = (selected) => `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><rect x="3.5" y="3.5" width="17" height="17" rx="4"></rect>${selected ? '<path d="m7.5 12.5 3 3 6-7"></path>' : ""}</svg>`;
  const bookEditIcon = () => '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="m4 20 4.1-1 10-10a2.1 2.1 0 0 0-3-3l-10 10L4 20Z"></path><path d="m13.7 7.3 3 3"></path></svg>';
  const send = (command, payload) => {
    const id = crypto.randomUUID();
    state.pendingCommands.set(id, command);
    window.chrome.webview.postMessage(JSON.stringify({ version: 1, id, command, ...(payload ? { payload } : {}) }));
    return id;
  };
  const setPdfLibraryFeedback = (message, isError = false) => {
    state.pdfLibraryFeedback = message;
    state.pdfLibraryFeedbackError = isError;
    const feedback = content.querySelector?.("[data-pdf-library-feedback]");
    if (!feedback) return;
    feedback.hidden = !message;
    feedback.textContent = message;
    feedback.classList?.toggle("is-error", isError);
    feedback.setAttribute?.("role", isError ? "alert" : "status");
  };
  const setPdfLibraryActionBusy = (target, busy) => {
    if (!target) return;
    target.disabled = busy;
    target.setAttribute?.("aria-busy", String(busy));
    const label = target.querySelector?.("[data-output-action-label]");
    if (label) label.textContent = busy ? valueFor(target.dataset, "outputBusyLabel", "Opening…") : valueFor(target.dataset, "outputIdleLabel", "Open");
  };
  const beginPdfLibraryAction = (command, payload, key, target) => {
    if (state.pdfLibraryPendingActions.has(key)) return;
    state.pdfLibraryPendingActions.add(key);
    const restoreFocus = document.activeElement === target;
    setPdfLibraryActionBusy(target, true);
    setPdfLibraryFeedback("Opening…");
    try {
      const requestId = send(command, payload);
      state.pdfLibraryRequestActions.set(requestId, { key, target, restoreFocus });
    } catch {
      state.pdfLibraryPendingActions.delete(key);
      setPdfLibraryActionBusy(target, false);
      setPdfLibraryFeedback("The output action could not be sent. Try again.", true);
    }
  };
  const finishPdfLibraryAction = (responseId) => {
    const action = state.pdfLibraryRequestActions.get(responseId);
    if (!action) return null;
    state.pdfLibraryRequestActions.delete(responseId);
    state.pdfLibraryPendingActions.delete(action.key);
    const selector = `[data-output-action-key="${CSS.escape(action.key)}"]`;
    const currentTarget = content.querySelector?.(selector);
    setPdfLibraryActionBusy(action.target, false);
    if (currentTarget && currentTarget !== action.target) setPdfLibraryActionBusy(currentTarget, false);
    const focusTarget = currentTarget ?? action.target;
    if (action.restoreFocus && focusTarget?.focus && (!document.activeElement || document.activeElement === document.body)) focusTarget.focus();
    return action;
  };
  const pdfLibraryActionError = (code) => ({
    invalid_output_action: "The output action was invalid. Refresh PDF Library and try again.",
    output_not_found: "This PDF was moved or deleted. Refresh PDF Library or rebuild the output.",
    output_not_previewable: "This PDF is invalid and cannot be previewed. Open its folder to inspect or rebuild it.",
    output_folder_not_found: "The Book output folder is unavailable. Refresh PDF Library or rebuild an output.",
    output_folder_inconsistent: "Cover and Interior are not in the expected Book output folder. Rebuild the outputs before opening the folder.",
    output_launch_failed: "Windows could not open this output. Check the file association or folder permissions and try again."
  })[code] ?? "The output action failed. Refresh PDF Library and try again.";
  const updatePhase = () => valueFor(state.updateSnapshot, "phase", "Idle");
  const updateIsBusy = () => ["Checking", "Downloading", "Verifying", "Installing"].includes(updatePhase());
  const stopUpdatePolling = () => { if (state.updatePollTimer !== null && window.clearInterval) window.clearInterval(state.updatePollTimer); state.updatePollTimer = null; };
  const startUpdatePolling = () => { if (state.updatePollTimer === null) state.updatePollTimer = window.setInterval(() => send("updates.getState"), 250); };
  const updateStageLabel = () => ({
    DownloadingArchive: "Downloading update…",
    DownloadingChecksum: "Downloading checksum…",
    Verifying: "Verifying update…",
    Extracting: "Extracting update…",
    Validating: "Validating update…",
    Ready: "Update ready"
  })[valueFor(state.updateSnapshot, "preparationStage", "")] ?? "";
  const updateVersion = (snapshot = state.updateSnapshot) => String(valueFor(snapshot, "latestVersion", valueFor(snapshot, "currentVersion", "")));
  const formatBytes = (value) => {
    const bytes = Math.max(0, Number(value) || 0);
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${Math.round(bytes / (1024 * 1024) * 10) / 10} MB`;
  };
  const dismissUpdateDialog = () => {
    state.updateDismissedVersion = updateVersion();
    renderUpdateDialog();
  };
  const renderUpdateDialog = () => {
    const root = document.getElementById("update-dialog-root"); if (!root) return;
    const snapshot = state.updateSnapshot; const phase = updatePhase();
    const canDownload = valueFor(snapshot, "canDownload", false); const canInstall = valueFor(snapshot, "canInstall", false);
    const retryAction = canInstall ? "install" : canDownload ? "download" : "";
    const versionKey = updateVersion(snapshot);
    const isDismissed = state.updateDismissedVersion !== "" && state.updateDismissedVersion === versionKey;
    const visible = Boolean(snapshot) && ((phase === "Available" && canDownload && !isDismissed) || ["Downloading", "Verifying", "Installing"].includes(phase) || (phase === "Ready" && canInstall && !isDismissed) || (phase === "Error" && retryAction));
    if (!visible) {
      const wasVisible = !root.hidden;
      root.hidden = true; root.innerHTML = "";
      if (wasVisible && state.updateDialogPreviousFocus?.focus) state.updateDialogPreviousFocus.focus();
      state.updateDialogPreviousFocus = null;
      return;
    }
    const wasHidden = root.hidden;
    if (wasHidden) state.updateDialogPreviousFocus = document.activeElement ?? null;
    const version = escapeHtml(versionKey); const releaseName = escapeHtml(valueFor(snapshot, "releaseName", "")); const notes = escapeHtml(valueFor(snapshot, "releaseNotes", ""));
    const archiveTotal = Number(valueFor(snapshot, "totalBytes", 0));
    const archiveStage = valueFor(snapshot, "preparationStage", "") === "DownloadingArchive";
    const percent = archiveStage && archiveTotal > 0 ? Math.max(0, Math.min(100, Math.round(Number(valueFor(snapshot, "bytesReceived", 0)) / archiveTotal * 100))) : null;
    const progress = percent !== null ? `<div class="update-progress" role="progressbar" aria-label="Download progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${percent}"><span style="width:${percent}%"></span></div><span class="update-progress-copy">${formatBytes(valueFor(snapshot, "bytesReceived", 0))} of ${formatBytes(archiveTotal)} (${percent}%)</span>`
      : ["Downloading", "Verifying"].includes(phase) ? `<div class="update-progress update-progress-indeterminate" aria-hidden="true"><span></span></div>` : "";
    const message = phase === "Available" ? `Printable Book ${version} is available.` : phase === "Ready" ? `Update ${version} is ready.` : phase === "Installing" ? `Restarting to install ${version}…` : phase === "Error" ? escapeHtml(valueFor(snapshot, "errorMessage", "Could not complete the update.")) : escapeHtml(updateStageLabel() || "Verifying update…");
    const title = phase === "Available" ? "Update available" : phase === "Ready" ? "Update ready" : phase === "Error" ? "Update needs attention" : "Preparing update";
    const actions = phase === "Available" ? '<button class="button-secondary" data-update-action="dismiss">Later</button><button class="button-primary" data-update-action="download" data-update-primary>Download update</button>'
      : phase === "Ready" ? '<button class="button-secondary" data-update-action="dismiss">Later</button><button class="button-primary" data-update-action="install" data-update-primary>Restart &amp; Update</button>'
      : phase === "Error" && retryAction ? `<button class="button-primary" data-update-action="${retryAction}" data-update-primary>${retryAction === "install" ? "Restart &amp; Update" : "Retry download"}</button>` : "";
    root.hidden = false;
    root.innerHTML = `<div class="update-dialog-backdrop" aria-hidden="true"></div><section class="update-dialog" role="dialog" aria-modal="true" aria-labelledby="update-dialog-title" aria-describedby="update-dialog-status"><h2 id="update-dialog-title">${title}</h2><div id="update-dialog-status" class="update-dialog-status" aria-live="polite"><strong>${message}</strong>${releaseName ? `<p>${releaseName}</p>` : ""}${progress}${notes ? `<div class="update-release-notes">${notes}</div>` : ""}</div><div class="update-dialog-actions">${actions}</div></section>`;
    if (wasHidden) root.querySelector?.("[data-update-primary]")?.focus?.();
  };
  const updateVersionLabel = () => {
    const versionLabel = document.querySelector(".pb-brand-version");
    const currentVersion = valueFor(state.updateSnapshot, "currentVersion", "");
    if (versionLabel && currentVersion) versionLabel.textContent = `Version ${currentVersion}`;
  };
  const applyUpdateSnapshot = (snapshot) => {
    const previousPhase = updatePhase();
    state.updateSnapshot = snapshot ?? null; state.updateCommandPending = "";
    if (["Downloading", "Verifying", "Ready", "Error"].includes(updatePhase()) && updatePhase() !== previousPhase) state.updateDismissedVersion = "";
    renderUpdateDialog(); updateVersionLabel();
    if (["Downloading", "Verifying", "Installing"].includes(updatePhase())) startUpdatePolling(); else stopUpdatePolling();
  };
  const beginUpdateCheck = () => { if (state.updateCommandPending !== "" || updateIsBusy()) return; state.updateCommandPending = "check"; send("updates.check", { trigger: "automatic" }); };
  const beginUpdateAction = (action) => {
    if (action === "dismiss") { if (!updateIsBusy()) dismissUpdateDialog(); return; }
    if (state.updateCommandPending !== "" || updateIsBusy()) return;
    if (action === "download") { state.updateDismissedVersion = ""; state.updateCommandPending = "download"; state.updateSnapshot = { ...(state.updateSnapshot ?? {}), phase: "Downloading", canCheck: false, canDownload: false, canInstall: false }; renderUpdateDialog(); updateVersionLabel(); startUpdatePolling(); send("updates.download"); }
    if (action === "install") { state.updateDismissedVersion = ""; state.updateCommandPending = "install"; state.updateSnapshot = { ...(state.updateSnapshot ?? {}), phase: "Installing", canCheck: false, canDownload: false, canInstall: false }; renderUpdateDialog(); updateVersionLabel(); startUpdatePolling(); send("updates.install"); }
  };
  const dateTime = (value) => value ? new Date(value).toLocaleString() : "—";
  const elapsedTime = (value) => {
    if (!value) return "—";
    const totalSeconds = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 1000));
    const minutes = Math.floor(totalSeconds / 60);
    const seconds = totalSeconds % 60;
    return `${minutes}:${String(seconds).padStart(2, "0")}`;
  };
  const fileSize = (bytes) => {
    const value = Number(bytes) || 0;
    if (value >= 1024 ** 3) return `${(value / (1024 ** 3)).toFixed(1)} GB`;
    if (value >= 1024 ** 2) return `${(value / (1024 ** 2)).toFixed(1)} MB`;
    if (value >= 1024) return `${Math.round(value / 1024)} KB`;
    return `${value} B`;
  };
  const inches = (value) => {
    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric.toFixed(2) : "—";
  };
  const panel = (title, body, extra = "") => `<section class="panel ${extra}"><h2 class="panel-title">${title}</h2>${body}</section>`;
  const currentRoute = () => document.querySelector(".nav-item-active")?.dataset.route ?? "books";
  const applicationIsLoading = () => state.applicationLoadState === "loading" || state.applicationLoadState === "refreshing";
  const processIsActive = () => valueFor(window.processSnapshot, "isActive", false) || valueFor(window.processSnapshot, "isCancelling", false);
  const processMode = (snapshot = window.processSnapshot) => {
    const value = valueFor(snapshot, "mode", null);
    if (value === 2) return "production-interior";
    if (value === 1) return "interior-only";
    if (value === 0) return "full-book";
    const normalized = String(value ?? "").replace(/[_\s]/g, "-").toLowerCase();
    if (normalized === "productioninterior" || normalized === "production-interior") return "production-interior";
    if (normalized === "interioronly" || normalized === "interior-only") return "interior-only";
    if (normalized === "fullbook" || normalized === "full-book") return "full-book";
    return "interior-only";
  };
  const productionInteriorIsRunning = () =>
    (state.processStartPending && state.productionFinalBuildActive)
    || (processIsActive() && processMode() === "production-interior");
  const processStartedAt = (snapshot) => {
    const value = valueFor(snapshot, "startedAt", "");
    const time = value ? new Date(value).getTime() : Number.NaN;
    return Number.isFinite(time) ? time : null;
  };
  const isStaleProcessSnapshot = (snapshot) => {
    const current = processStartedAt(window.processSnapshot);
    const incoming = processStartedAt(snapshot);
    return current !== null && incoming !== null && incoming < current;
  };
  const cacheCleanupBlocked = () => applicationIsLoading() || processIsActive() || state.cacheCleanupActive || Boolean(state.productionActionTaskId);
  const updateGlobalRefreshControl = () => {
    const refreshButton = document.getElementById("refresh-button");
    if (!refreshButton) return;
    const loading = applicationIsLoading();
    refreshButton.disabled = loading;
    refreshButton.setAttribute("aria-busy", String(loading));
    refreshButton.textContent = state.applicationLoadState === "refreshing" ? "Refreshing…" : state.applicationLoadState === "loading" ? "Loading…" : "Refresh";
  };
  const refreshAction = (label = "Refresh", disabled = false) => `<button class="button-secondary" type="button" data-action="refresh" ${applicationIsLoading() || disabled ? "disabled" : ""}>${state.applicationLoadState === "refreshing" ? "Refreshing…" : label}</button>`;
  const renderLoadFailure = () => `<section class="panel" role="alert"><h2 class="panel-title">Unable to load library</h2><p class="panel-note">${escapeHtml(state.applicationLoadError || "Application refresh failed.")}</p><div class="page-actions mt-4"><button class="button-primary" data-action="refresh">Retry</button></div></section>`;
  const renderRefreshFailure = () => `<section class="refresh-failure" role="alert"><span>Refresh failed</span><span>${escapeHtml(state.applicationLoadError || "Application refresh failed.")}</span><button class="button-secondary" data-action="refresh">Retry</button></section>`;
  const beginApplicationRefresh = () => {
    if (applicationIsLoading()) return;
    state.applicationLoadState = window.appSnapshot ? "refreshing" : "loading";
    state.applicationLoadError = "";
    state.libraryRefreshTaskId = "";
    state.libraryRefreshResultRequested = false;
    if (state.productionRefreshAwaitingSnapshot && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
      updateGlobalRefreshControl();
      updateProductionInteractionUi();
    } else {
      render(currentRoute(), false);
    }
    send("app.refresh");
  };
  const pollLibraryRefresh = () => {
    if (state.libraryRefreshTaskId) send("task.get", { taskId: state.libraryRefreshTaskId });
  };
  const observeLibraryRefresh = (task) => {
    const taskId = valueFor(task, "taskId", "");
    if (!taskId) return;
    state.libraryRefreshTaskId = taskId;
    const taskState = valueFor(task, "state", "Queued");
    if (["Queued", "Running", "Cancelling"].includes(taskState)) {
      if (state.libraryRefreshPollTimer === null) state.libraryRefreshPollTimer = window.setInterval(pollLibraryRefresh, 250);
      return;
    }
    if (state.libraryRefreshPollTimer !== null && window.clearInterval) window.clearInterval(state.libraryRefreshPollTimer);
    state.libraryRefreshPollTimer = null;
    if (taskState === "Completed" && !state.libraryRefreshResultRequested) {
      state.libraryRefreshResultRequested = true;
      if (taskId === state.bookInteriorSaveTaskId) state.bookInteriorSaveAwaitingSnapshot = true;
      if (taskId === state.interiorShuffleTaskId) state.interiorShuffleAwaitingSnapshot = true;
      send("app.refresh.result", { taskId });
      return;
    }
    if (state.keywordBuilderRefreshPending) {
      state.keywordBuilderRefreshPending = false;
      state.keywordBuilderRefreshNeeded = true;
      state.catalogFeedback = "Saved. Library refresh failed; retry refresh.";
      state.catalogFeedbackError = false;
      state.applicationLoadState = "ready";
      state.applicationLoadError = "";
      if (state.bookDrawerOpen && currentRoute() === "books") refreshBookKeywordBuilderCard();
      status.textContent = "Keyword Builder saved; refresh needed";
      return;
    }
    if (state.brandCloneAwaitingSnapshot) {
      state.brandClonePending = false;
      state.brandCloneFeedback = "Brand was cloned, but the refreshed library could not be loaded. Use Refresh to retry.";
      state.brandCloneFeedbackError = true;
      state.applicationLoadState = "ready";
      state.applicationLoadError = "";
      if (currentRoute() === "brands") render("brands", false);
      status.textContent = "Brand clone refresh needs attention";
      return;
    }
    if (state.bookCloneAwaitingSnapshot) {
      state.bookClonePending = false;
      state.bookCloneFeedback = "Book was cloned, but the refreshed library could not be loaded. Use Refresh to retry.";
      state.bookCloneFeedbackError = true;
      state.applicationLoadState = "ready";
      state.applicationLoadError = "";
      if (currentRoute() === "books") render("books", false);
      status.textContent = "Book clone refresh needs attention";
      return;
    }
    if (taskId === state.interiorShuffleTaskId) {
      state.interiorShuffleTaskId = "";
      state.interiorShufflePending = false;
      state.interiorShuffleFeedback = "Random order was saved, but its refreshed status could not be loaded. Use Refresh to retry.";
      state.interiorShuffleFeedbackError = true;
      state.applicationLoadState = "ready";
      state.applicationLoadError = "";
      if (state.bookDrawerOpen && state.selectedBookTab === "artwork" && currentRoute() === "books") refreshInteriorArtworkWorkspace();
      status.textContent = "Interior status refresh failed";
      return;
    }
    state.applicationLoadState = "failed";
    state.applicationLoadError = valueFor(task, "errorMessage", "Application refresh failed.");
    render(currentRoute(), false);
  };
  const pollCacheCleanup = () => {
    if (state.cacheCleanupTaskId) send("task.get", { taskId: state.cacheCleanupTaskId });
  };
  const observeCacheCleanup = (task) => {
    const taskId = valueFor(task, "taskId", "");
    if (!taskId) return;
    state.cacheCleanupTaskId = taskId;
    const taskState = valueFor(task, "state", "Queued");
    if (["Queued", "Running", "Cancelling"].includes(taskState)) {
      state.cacheCleanupActive = true;
      if (state.cacheCleanupPollTimer === null) state.cacheCleanupPollTimer = window.setInterval(pollCacheCleanup, 250);
      if (currentRoute() === "books") render("books", false);
      return;
    }
    if (state.cacheCleanupPollTimer !== null && window.clearInterval) window.clearInterval(state.cacheCleanupPollTimer);
    state.cacheCleanupPollTimer = null;
    state.cacheCleanupActive = false;
    if (taskState === "Completed" && !state.cacheCleanupResultRequested) {
      state.cacheCleanupResultRequested = true;
      send("cache.clear.result", { taskId });
      return;
    }
    state.cacheCleanupTaskId = "";
    state.cacheCleanupResultRequested = false;
    status.textContent = taskState === "Cancelled" ? "Cache cleanup cancelled" : valueFor(task, "errorMessage", "Cache cleanup failed.");
    if (currentRoute() === "books") render("books", false);
  };
  const pollProductionAction = () => {
    if (state.productionActionTaskId) send("task.get", { taskId: state.productionActionTaskId });
  };
  const productionActionActive = () => Boolean(state.productionActionTaskId);
  const updateProductionInteractionUi = () => {
    const workspace = document.querySelector(".production-workspace");
    if (!workspace) return;
    const taskBusy = productionActionActive();
    const controlsBusy = taskBusy || processIsActive() || state.cacheCleanupActive || applicationIsLoading();
    const finalBusy = productionInteriorIsRunning();
    workspace.setAttribute("aria-busy", String(taskBusy || finalBusy));
    const feedback = workspace.querySelector?.("[data-production-feedback]");
    if (feedback) {
      feedback.hidden = !state.productionFeedback;
      feedback.textContent = state.productionFeedback;
      feedback.classList.toggle("is-error", state.productionFeedbackError);
      feedback.classList.toggle("is-warning", state.productionFeedbackWarning);
      feedback.setAttribute("role", state.productionFeedbackError || state.productionFeedbackWarning ? "alert" : "status");
      feedback.setAttribute("aria-atomic", "true");
    }
    workspace.querySelectorAll?.('[data-action="upload-production-asset"]').forEach((button) => {
      const importing = state.productionImportPending === button.dataset.productionAsset;
      button.disabled = controlsBusy || importing;
      button.textContent = importing ? "Selecting…" : button.dataset.productionIdleLabel;
    });
    workspace.querySelectorAll?.('[data-action="start-production-action"]').forEach((button) => {
      const active = taskBusy && state.productionActionName === button.dataset.productionAction;
      button.disabled = button.dataset.productionSourceExists !== "true" || controlsBusy;
      button.textContent = active ? "Working…" : button.dataset.productionIdleLabel;
      button.setAttribute("aria-busy", String(active));
    });
    const finalButton = workspace.querySelector?.('[data-action="build-final-interior"]');
    if (finalButton) {
      finalButton.disabled = finalButton.dataset.productionReady !== "true" || processIsActive() || state.processStartPending || applicationIsLoading();
      finalButton.textContent = finalBusy ? "Building…" : "Build Final Interior";
      finalButton.setAttribute("aria-busy", String(finalBusy));
    }
  };
  const observeProductionAction = (task) => {
    const taskId = valueFor(task, "taskId", "");
    if (!taskId) return;
    const taskState = valueFor(task, "state", "Queued");
    state.productionActionTaskId = taskId;
    state.productionFeedback = valueFor(task, "step", "Preparing Production asset…") || "Preparing Production asset…";
    state.productionFeedbackError = false;
    state.productionFeedbackWarning = false;
    if (["Queued", "Running", "Cancelling"].includes(taskState)) {
      if (state.productionActionPollTimer === null) state.productionActionPollTimer = window.setInterval(pollProductionAction, 250);
      if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      return;
    }
    if (state.productionActionPollTimer !== null && window.clearInterval) window.clearInterval(state.productionActionPollTimer);
    state.productionActionPollTimer = null;
    state.productionActionTaskId = "";
    state.productionActionName = "";
    const panelPreviewWarning = taskState === "Completed" && valueFor(task, "detail", "") === "cover_panel_previews_unavailable";
    state.productionFeedbackError = taskState === "Failed";
    state.productionFeedbackWarning = panelPreviewWarning;
    state.productionFeedback = panelPreviewWarning
      ? "Cover PDF built. back_cover.jpg and front_cover.jpg are unavailable. Close open preview files, check Output folder permissions, then run Build Cover PDF again. Refreshing status…"
      : taskState === "Completed"
        ? "Production action completed. Refreshing status…"
      : taskState === "Cancelled"
        ? "Production action cancelled."
        : valueFor(task, "errorMessage", "Production action failed. The previous asset or PDF was kept.");
    if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
    if (taskState === "Completed") {
      state.productionRefreshAwaitingSnapshot = true;
      beginApplicationRefresh();
    }
  };
  const selectedBook = () => books().find((book) => bookId(book) === state.selectedBookId);
  const assignmentReadiness = (summary) => {
    const assigned = assignedBrandName(summary);
    if (!assigned) return { ready: false, reason: "Assign a Brand to this Book before continuing." };
    if (assignmentStatus(summary) !== "Valid") return { ready: false, reason: valueFor(summary, "assignmentReason", "This Book's Brand assignment is invalid. Reassign or unassign it before continuing.") };
    const brand = assignedBrandFor(summary);
    if (!brand) return { ready: false, reason: `Assigned Brand '${assigned}' is no longer available. Reassign this Book before continuing.` };
    return { ready: true, reason: "", brand };
  };
  const assignedBrandExecutionReadiness = (summary) => {
    const assignment = assignmentReadiness(summary);
    if (!assignment.ready) return assignment;
    if (!isValidatedBrand(assignment.brand)) return { ready: false, reason: `Validate assigned Brand '${assignedBrandName(summary)}' before continuing.`, brand: assignment.brand };
    return assignment;
  };
  const brandTemplateCopyReadiness = (book, summary) => {
    if (!book || !summary || valueFor(summary, "validationStatus", "") !== "Ready") return { ready: false, reason: "Run Interior preflight and resolve Book errors first." };
    const assignment = assignedBrandExecutionReadiness(summary);
    if (!assignment.ready) return assignment;
    const brand = assignment.brand;
    return { ready: true, reason: `Copy cover.psd, app_plus.psd, and book_owner.psd from ${valueFor(brand, "name", "the assigned Brand")}.`, brand };
  };
  const assetsFor = (summary) => valueFor(summary, "assets", []);
  const assetForReference = (summary, sourceReference) => assetsFor(summary).find((asset) => valueFor(asset, "sourceReference", "") === sourceReference);
  const interiorDraftFor = (id, create = false) => {
    let draft = state.bookInteriorDrafts.get(id);
    if (!draft && create) { draft = { assets: new Map() }; state.bookInteriorDrafts.set(id, draft); }
    return draft ?? null;
  };
  const clearInteriorDraft = (id) => state.bookInteriorDrafts.delete(id);
  const clearArtworkBulkSelection = () => {
    state.selectedArtworkReferences.clear();
    state.assetBulkActive = "unchanged";
    state.assetBulkFrameMode = "unchanged";
  };
  const hasInteriorDraft = (id) => {
    const draft = interiorDraftFor(id);
    return Boolean(draft && (draft.hasBackground !== undefined || draft.hasIntro !== undefined || draft.introSourceReferences !== undefined || draft.assets.size));
  };
  const effectiveBackground = (book, summary) => {
    const draft = interiorDraftFor(bookId(book));
    return draft?.hasBackground ?? valueFor(summary, "hasBackground", true);
  };
  const effectiveInteriorAsset = (book, asset) => {
    const change = interiorDraftFor(bookId(book))?.assets.get(valueFor(asset, "sourceReference", ""));
    return {
      isActive: change?.active ?? valueFor(asset, "isActive", true),
      frameMode: change?.frameMode ?? frameModeValue(valueFor(asset, "frameMode", "disabled"))
    };
  };
  const trimEmptyInteriorDraft = (id, draft) => { if (draft.hasBackground === undefined && draft.hasIntro === undefined && draft.introSourceReferences === undefined && draft.assets.size === 0) clearInteriorDraft(id); };
  const stageBackgroundChange = (book, summary, enabled) => {
    const id = bookId(book);
    const draft = interiorDraftFor(id, true);
    if (enabled === valueFor(summary, "hasBackground", true)) delete draft.hasBackground;
    else draft.hasBackground = enabled;
    trimEmptyInteriorDraft(id, draft);
  };
  const stageInteriorAssetChange = (book, asset, field, value) => {
    const id = bookId(book);
    const reference = valueFor(asset, "sourceReference", "");
    const draft = interiorDraftFor(id, true);
    const change = draft.assets.get(reference) ?? {};
    const original = field === "active" ? valueFor(asset, "isActive", true) : frameModeValue(valueFor(asset, "frameMode", "disabled"));
    if (value === original) delete change[field]; else change[field] = value;
    if (change.active === undefined && change.frameMode === undefined) draft.assets.delete(reference); else draft.assets.set(reference, change);
    trimEmptyInteriorDraft(id, draft);
  };
  const persistedIntroSourceReferences = (summary) => {
    const sources = [
      ...(valueFor(summary, "interiorSourcePages", []) ?? []).map((page) => ({ sourceKey: valueFor(page, "sourceKey", ""), sourceReference: valueFor(page, "sourceReference", "") })),
      ...assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior").map((asset) => ({ sourceKey: valueFor(asset, "relativePath", ""), sourceReference: valueFor(asset, "sourceReference", "") }))
    ];
    return (valueFor(summary, "selectedIntroInteriorSourceKeys", []) ?? []).map((sourceKey) =>
      sources.find((source) => String(source.sourceKey).toLowerCase() === String(sourceKey).toLowerCase())?.sourceReference ?? sourceKey);
  };
  const effectiveIntro = (book, summary) => {
    const draft = interiorDraftFor(bookId(book));
    return {
      hasIntro: draft?.hasIntro ?? valueFor(summary, "hasIntro", false),
      sourceReferences: draft?.introSourceReferences ?? persistedIntroSourceReferences(summary)
    };
  };
  const introTemplateAssetId = (asset, brandName = "") => encodeURIComponent(`${brandName}\u0000${valueFor(asset, "key", "")}`);
  const finalInteriorPageSize = () => {
    const settings = valueFor(window.appSnapshot, "globalSettings", {});
    return {
      width: Number(valueFor(settings, "finalPageWidth", 2588)),
      height: Number(valueFor(settings, "finalPageHeight", 2625))
    };
  };
  const introTemplateSizeDescription = () => {
    const finalPage = finalInteriorPageSize();
    return `1024 × 1024, 2048 × 2048, or ${finalPage.width} × ${finalPage.height} pixels`;
  };
  const isSupportedIntroTemplateSize = (width, height) => {
    const finalPage = finalInteriorPageSize();
    return (width === 1024 && height === 1024) ||
      (width === 2048 && height === 2048) ||
      (width === finalPage.width && height === finalPage.height);
  };
  const introReadiness = (book, summary) => {
    const selection = effectiveIntro(book, summary);
    const customCandidates = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
    if (selection.hasIntro) {
      if (!selection.sourceReferences.length) return { ready: false, reason: "Custom Intro mode needs at least one selected Book interior page." };
      const sources = new Set(customCandidates.map((asset) => String(valueFor(asset, "sourceReference", "")).toLowerCase()));
      if (selection.sourceReferences.some((reference) => !sources.has(String(reference).toLowerCase()))) return { ready: false, reason: "A selected custom Intro page is missing from Book interior." };
      return { ready: true, reason: "" };
    }
    const assignment = assignmentReadiness(summary);
    if (!assignment.ready) return assignment;
    const brand = assignment.brand;
    const templates = (valueFor(brand, "introTemplateAssets", []) ?? []).filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", "")));
    if (!templates.length) return { ready: false, reason: `Assigned Brand '${assignedBrandName(summary)}' has no eligible Intro templates.` };
    const effectiveTemplates = templates;
    if (effectiveTemplates.some((asset) => state.introTemplateDimensions.get(introTemplateAssetId(asset, valueFor(brand, "name", "")))?.valid === false)) return { ready: false, reason: `An effective Intro template is unreadable or must be ${introTemplateSizeDescription()}.` };
    return { ready: true, reason: "" };
  };
  const processingReadiness = (book, summary) => {
    if (!workspaceStateAvailable(summary)) return { ready: false, reason: valueFor(summary, "workspaceStateError", "Workspace state is unavailable. Restore or repair the state file, then refresh.") };
    if (hasInteriorDraft(bookId(book))) return { ready: false, reason: "Save Interior changes before processing." };
    if (String(valueFor(valueFor(summary, "interiorShuffle", {}), "status", "Missing")) !== "Current") return { ready: false, reason: "Random Interior using the current active artwork before processing." };
    if (valueFor(summary, "validationStatus", "Needs review") !== "Ready") return { ready: false, reason: "Run Interior preflight until this Book is ready." };
    const assignment = assignedBrandExecutionReadiness(summary);
    if (!assignment.ready) return assignment;
    return introReadiness(book, summary);
  };
  const productionSummaryFor = (summary) => valueFor(summary, "production", { assets: [] });
  const productionAssetFor = (summary, kind) => valueFor(productionSummaryFor(summary), "assets", [])
    .find((asset) => valueFor(asset, "assetKind", "") === kind) ?? null;
  const productionFinalReadiness = (book, summary) => {
    if (hasInteriorDraft(bookId(book))) return { ready: false, reason: "Save changes before building Final Interior." };
    const standard = processingReadiness(book, summary);
    if (!standard.ready) return standard;
    const brand = standard.brand ?? assignedBrandFor(summary);
    const missing = [["interior-cover", "Interior Cover"], ["book-owner", "Book Owner"]]
      .filter(([kind]) => valueFor(productionAssetFor(summary, kind), "sourceStatus", "Missing") === "Missing")
      .map(([, label]) => label);
    if (missing.length) return { ready: false, reason: `Upload ${missing.join(" and ")} before building Final Interior.` };
    if (processIsActive() || state.processStartPending) return { ready: false, reason: productionInteriorIsRunning() ? "Build Final Interior is already running." : "Process Interior is running." };
    if (productionActionActive()) return { ready: false, reason: "Wait for the active Production action to finish." };
    if (state.cacheCleanupActive) return { ready: false, reason: "Wait for Clear Cache to finish." };
    return { ready: true, reason: "Build a fresh Production Interior from current sources and saved settings.", brand };
  };

  const selectedProcessingReadiness = () => {
    const selected = [...state.selectedBookIds]
      .map((id) => books().find((book) => bookId(book) === id))
      .filter(Boolean);
    for (const book of selected) {
      const readiness = processingReadiness(book, summaryFor(book));
      if (!readiness.ready) return { ready: false, reason: `${valueFor(book, "name", "Book")}: ${readiness.reason}`, book };
    }
    const groups = new Map();
    selected.forEach((book) => {
      const brandName = assignedBrandName(summaryFor(book));
      groups.set(brandName, (groups.get(brandName) ?? 0) + 1);
    });
    if (groups.size > 1) {
      const detail = [...groups.entries()].map(([brand, count]) => `${brand}: ${count}`).join("; ");
      return { ready: false, reason: `Selected Books belong to different Brands (${detail}). Filter and process one Brand at a time.`, mixed: true };
    }
    return { ready: selected.length > 0, reason: selected.length ? "" : "Select at least one Book.", brandName: groups.keys().next().value ?? "" };
  };
  const sameKeys = (left, right) => left.length === right.length && left.every((key, index) => key.toLowerCase() === String(right[index] ?? "").toLowerCase());
  const stageIntroChange = (book, summary, hasIntro, sourceReferences) => {
    const id = bookId(book);
    const draft = interiorDraftFor(id, true);
    const originalHasIntro = valueFor(summary, "hasIntro", false);
    const originalSources = persistedIntroSourceReferences(summary);
    if (hasIntro === originalHasIntro) delete draft.hasIntro; else draft.hasIntro = hasIntro;
    if (!hasIntro) delete draft.introSourceReferences;
    else if (sameKeys(sourceReferences, originalSources)) delete draft.introSourceReferences;
    else draft.introSourceReferences = [...sourceReferences];
    trimEmptyInteriorDraft(id, draft);
  };
  const interiorSavePayload = (id) => {
    const draft = interiorDraftFor(id);
    if (!draft) return null;
    const assets = [...draft.assets].map(([sourceReference, change]) => ({ sourceReference, ...(change.active !== undefined ? { active: change.active } : {}), ...(change.frameMode !== undefined ? { frameMode: change.frameMode } : {}) }));
    return { bookId: id, ...(draft.hasBackground !== undefined ? { hasBackground: draft.hasBackground } : {}), ...(draft.hasIntro !== undefined ? { hasIntro: draft.hasIntro } : {}), ...(draft.introSourceReferences !== undefined ? { introSourceReferences: draft.introSourceReferences } : {}), assets };
  };
  const updateInteriorSaveUi = () => {
    const id = state.selectedBookId;
    const dirty = hasInteriorDraft(id);
    const controlsDisabled = processIsActive() || state.bookInteriorSavePending;
    const save = document.querySelector('[data-action="save-book-interior-settings"]');
    if (save) { save.disabled = !dirty || controlsDisabled; save.setAttribute("aria-busy", String(state.bookInteriorSavePending)); save.textContent = state.bookInteriorSavePending ? "Saving…" : "Save changes"; }
    document.querySelectorAll('[data-action="set-book-background"], [data-action="set-intro-mode"], [data-action="intro-add-template"], [data-action="intro-remove-template"], [data-action="intro-move-template"], [data-action="toggle-artwork-selection"], [data-action="toggle-all-artwork"], [data-action="set-artwork-bulk-active"], [data-action="set-artwork-bulk-frame-mode"], [data-action="apply-artwork-bulk"]').forEach((control) => { control.disabled = controlsDisabled || control.dataset.customIntro === "true"; });
    const indicator = document.querySelector("[data-book-interior-unsaved]");
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if (indicator) indicator.hidden = !(dirty || (book && summary && (hasMetadataDraft(book, summary) || hasKeywordBuilderDraft(book, summary))));
  };
  const localImageMarkup = (asset, alt, fallback = "Preview unavailable") => {
    const url = valueFor(asset, "localImageUrl", "");
    return url
      ? `<img src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" width="256" height="256" loading="lazy" decoding="async" data-local-image data-image-fallback="${escapeHtml(fallback)}">`
      : `<span class="book-preview-fallback" aria-label="${escapeHtml(fallback)}">${escapeHtml(fallback)}</span>`;
  };
  const introTemplateImageMarkup = (asset, alt, brandName, fallback = "Image unavailable") => {
    const url = valueFor(asset, "localImageUrl", "");
    return url
      ? `<img src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" width="256" height="256" loading="lazy" decoding="async" data-local-image data-intro-template-id="${escapeHtml(introTemplateAssetId(asset, brandName))}" data-image-fallback="${escapeHtml(fallback)}">`
      : `<span class="book-preview-fallback" aria-label="${escapeHtml(fallback)}">${escapeHtml(fallback)}</span>`;
  };
  const bookThumbnailMarkup = (book, summary, fallback = "Preview unavailable") => {
    const cover = assetForReference(summary, valueFor(summary, "representativeCoverReference", ""));
    return localImageMarkup(cover, `Cover for ${valueFor(book, "name", bookId(book))}`, fallback);
  };
  const pdfLibraryCoverThumbnailMarkup = (book, summary, fallback = "Cover unavailable") => {
    const coverOutput = pdfLibraryOutputs(summary).find((output) => valueFor(output, "artifactKind", "") === "Cover");
    const url = valueFor(coverOutput, "thumbnailImageUrl", "");
    return url
      ? `<img src="${escapeHtml(url)}" alt="Final Cover PDF for ${escapeHtml(valueFor(book, "name", bookId(book)))}" width="2726" height="1313" loading="lazy" decoding="async" data-local-image data-image-fallback="${escapeHtml(fallback)}">`
      : bookThumbnailMarkup(book, summary, fallback);
  };
  const assetDimensions = (asset) => {
    const width = valueFor(asset, "width", null);
    const height = valueFor(asset, "height", null);
    return width && height ? `${width} × ${height}` : "Dimensions unavailable";
  };
  const processStepKey = (step) => String(step ?? "").trim().toLowerCase();
  const processStepLabel = (mode, step) => {
    const key = processStepKey(step);
    const shared = {
      queued: "Preparing",
      preparing: "Preparing",
      "intro-pages": "Intro pages",
      "interior-pages": "Interior pages",
      assembly: "Validating pages",
      completed: "Completed",
      cancelled: "Cancelled",
      failed: "Failed",
      cancelling: "Cancelling"
    };
    if (mode === "production-interior" || mode === "full-book") {
      return {
        ...shared,
        "production-prefix-pages": "Production pages",
        "interior-pdf-export": "PDF export",
        "pdf-export": "PDF export",
        "interior-publish": "Publishing",
        publish: "Publishing"
      }[key] ?? step ?? "Preparing";
    }
    return { ...shared, "book.completed": "Saving previews" }[key] ?? step ?? "Preparing";
  };
  const processStageModel = (mode, step, terminalStep = "") => {
    const production = mode === "production-interior" || mode === "full-book";
    const stages = production
      ? ["Preparing", "Production pages", "Intro pages", "Interior pages", "Validating pages", "PDF export", "Publishing"]
      : ["Preparing", "Intro pages", "Interior pages", "Validating pages", "Saving previews"];
    if (terminalStep === "Completed") return { stages, index: stages.length };
    const label = processStepLabel(mode, step);
    const index = stages.indexOf(label);
    return { stages, index: index >= 0 ? index : 0 };
  };
  const updateGlobalProcessStatus = () => {
    const control = document.getElementById("global-process-status");
    if (!control) return;
    const snapshot = window.processSnapshot;
    const active = valueFor(snapshot, "isActive", false);
    const cancelling = valueFor(snapshot, "isCancelling", false);
    const storageBookId = String(valueFor(state.storageSnapshot, "activeBookId", "") ?? "");
    const storageBook = valueFor(state.storageSnapshot, "books", []).find((book) => String(valueFor(book, "bookId", "")) === storageBookId);
    const storageSession = valueFor(storageBook, "session", null);
    const storageActive = Boolean(storageBookId && valueFor(storageSession, "isActive", false));
    const storageView = valueFor(storageSession, "view", {});
    const storageCancelling = Boolean(valueFor(storageSession, "isCancelling", false));
    const step = processStepLabel(processMode(snapshot), valueFor(snapshot, "currentStep", "Preparing"));
    const activeLabel = processMode(snapshot) === "production-interior" ? `Building Final Interior · ${step}` : `Process Interior · ${step}`;
    control.classList?.toggle("is-active", (active && !cancelling) || (storageActive && !storageCancelling));
    control.classList?.toggle("is-cancelling", cancelling || storageCancelling);
    const storageLabel = storageCancelling
      ? `Stopping S3 · ${storageBookId}`
      : `${storageActionName(valueFor(storageView, "action", "Check"))} S3 · ${storageBookId}`;
    control.innerHTML = `<span class="status-dot"></span><span>${escapeHtml(cancelling ? "Stopping processing" : active ? activeLabel : storageActive ? storageLabel : "Nothing processing")}</span>`;
  };

  const renderConfiguration = () => {
    ensureGenericKeywordDrafts();
    ensureAmazonMarketplaceDrafts();
    const settings = valueFor(window.appSnapshot, "globalSettings", {});
    const setting = (name, fallback) => valueFor(settings, name, fallback);
    const grouped = (group, name, fallback) => valueFor(valueFor(settings, group, {}), name, fallback);
    const detectionInput = (label, name, fallback, extra = "") => `<label class="field"><span>${label}</span><input class="control" data-setting-group="borderLineDetection" data-setting="${name}" type="number" ${extra} value="${grouped("borderLineDetection", name, fallback)}"></label>`;
    const group = (id, title, description, fields, wide = false) => `<fieldset class="configuration-group ${wide ? "configuration-group-wide" : ""}" aria-describedby="${id}-help"><legend>${title}</legend><p id="${id}-help">${description}</p>${fields}</fieldset>`;
    const genericKeywordLanguage = supportedLanguages().find((language) => String(valueFor(language, "code", "")).toLocaleLowerCase() === state.settingsGenericLanguageCode) ?? supportedLanguages()[0];
    const genericKeywordText = state.settingsGenericKeywordDrafts.get(state.settingsGenericLanguageCode) ?? "";
    const amazonLanguage = supportedLanguages().find((language) => String(valueFor(language, "code", "")).toLocaleLowerCase() === state.settingsAmazonLanguageCode) ?? supportedLanguages()[0];
    const amazonProfile = state.settingsAmazonProfileDrafts.get(state.settingsAmazonLanguageCode) ?? {};
    const feedback = state.settingsFeedback || "Ready";
    const feedbackState = state.settingsFeedbackError ? "error" : state.settingsSavePending ? "saving" : feedback === "Saved" ? "saved" : "ready";
    const runtime = `<div class="configuration-field-grid"><label class="field"><span>Maximum concurrency</span><input class="control" data-setting="maximumPageConcurrency" type="number" min="1" max="12" value="${setting("maximumPageConcurrency", 4)}"></label></div>`;
    const artworkPreparation = `<div class="configuration-field-grid three"><label class="field"><span>Artwork dark threshold</span><input class="control" data-setting="artworkDetectionThreshold" type="number" min="0" max="255" value="${setting("artworkDetectionThreshold", 20)}"></label><label class="field"><span>Maximum artwork side (px)</span><input class="control" data-setting="artworkMaximumSide" type="number" min="1" value="${setting("artworkMaximumSide", 2270)}"></label><label class="field"><span>Normalized source size (px)</span><input class="control" data-setting-group="artworkSourceNormalization" data-setting="normalizedSourceSize" type="number" min="1" value="${grouped("artworkSourceNormalization", "normalizedSourceSize", 2048)}"></label></div>`;
    const keywordDefaults = `<div class="configuration-field-grid two"><label class="field" for="generic-keywords-language"><span>Language</span><select id="generic-keywords-language" class="control" data-action="generic-keyword-language" data-generic-keyword-language>${supportedLanguages().map((language) => { const code = String(valueFor(language, "code", "")).toLocaleLowerCase(); return `<option value="${escapeHtml(code)}" ${code === state.settingsGenericLanguageCode ? "selected" : ""}>${escapeHtml(valueFor(language, "name", code))} (${escapeHtml(code)})</option>`; }).join("")}</select><small>Select the language profile to edit.</small></label><label class="field keyword-settings-field" for="generic-keywords-input"><span>Generic Keywords · <strong data-generic-keyword-language-label>${escapeHtml(valueFor(genericKeywordLanguage, "name", state.settingsGenericLanguageCode))} (${escapeHtml(state.settingsGenericLanguageCode)})</strong></span><textarea id="generic-keywords-input" class="control keyword-list-input" rows="5" data-generic-keywords aria-describedby="generic-keywords-help" autocomplete="off" spellcheck="false">${escapeHtml(genericKeywordText)}</textarea><small id="generic-keywords-help">One phrase per line. Books use only the profile matching their persisted Language; empty profiles do not fall back.</small></label></div>`;
    const amazonMarkets = `<div class="configuration-field-grid two"><label class="field" for="amazon-market-language"><span>Language</span><select id="amazon-market-language" class="control" data-action="amazon-market-language" data-amazon-market-language>${supportedLanguages().map((language) => { const code = String(valueFor(language, "code", "")).toLocaleLowerCase(); return `<option value="${escapeHtml(code)}" ${code === state.settingsAmazonLanguageCode ? "selected" : ""}>${escapeHtml(valueFor(language, "name", code))} (${escapeHtml(code)})</option>`; }).join("")}</select><small>Select the Amazon market profile to edit.</small></label><div class="field"><span>Editing market</span><strong data-amazon-market-language-label>${escapeHtml(valueFor(amazonLanguage, "name", state.settingsAmazonLanguageCode))} (${escapeHtml(state.settingsAmazonLanguageCode)})</strong><small>The Book Language selects this profile automatically.</small></div></div><div class="configuration-field-grid two"><label class="field" for="amazon-profile-key"><span>Browser profile key</span><input id="amazon-profile-key" class="control" data-amazon-profile-field="profileKey" value="${escapeHtml(valueFor(amazonProfile, "profileKey", ""))}" required pattern="[a-z0-9][a-z0-9-]{0,31}" autocomplete="off" spellcheck="false"><small>Lowercase letters, numbers, and hyphens. Must be unique.</small></label><label class="field" for="amazon-locale"><span>Browser locale</span><input id="amazon-locale" class="control" data-amazon-profile-field="locale" value="${escapeHtml(valueFor(amazonProfile, "locale", ""))}" required autocomplete="off" spellcheck="false"><small>Locale passed to the browser context, for example de-DE.</small></label><label class="field" for="amazon-base-url"><span>Amazon Base URL</span><input id="amazon-base-url" class="control" data-amazon-profile-field="baseUrl" type="url" value="${escapeHtml(valueFor(amazonProfile, "baseUrl", ""))}" required autocomplete="off" spellcheck="false"><small>HTTPS marketplace origin for this language, without a path or query.</small></label><label class="field" for="amazon-title-terms"><span>Title Terms</span><input id="amazon-title-terms" class="control" data-amazon-profile-field="titleTerms" value="${escapeHtml(valueFor(amazonProfile, "titleTerms", ""))}" required autocomplete="off" spellcheck="false"><small>Comma-separated title phrases used to accept ASIN candidates.</small></label></div>`;
    const workingCanvas = `<div class="configuration-field-grid two"><label class="field"><span>Working Area width (px)</span><input class="control" data-setting="workingPageWidth" type="number" min="1" value="${setting("workingPageWidth", 2550)}"></label><label class="field"><span>Working Area height (px)</span><input class="control" data-setting="workingPageHeight" type="number" min="1" value="${setting("workingPageHeight", 2550)}"></label></div>`;
    const finalOutput = `<div class="configuration-field-grid three"><label class="field"><span>Final Page width (px)</span><input class="control" data-setting="finalPageWidth" type="number" min="1" value="${setting("finalPageWidth", 2588)}"></label><label class="field"><span>Final Page height (px)</span><input class="control" data-setting="finalPageHeight" type="number" min="1" value="${setting("finalPageHeight", 2625)}"></label><label class="field"><span>Output DPI</span><input class="control" data-setting="dpi" type="number" min="1" value="${setting("dpi", 300)}"></label></div>`;
    const borderRange = `<div class="configuration-field-grid three">${detectionInput("Pass 1 depth", "pass1SearchDepth", 200, "min=1")}${detectionInput("Pass 2 depth", "pass2SearchDepth", 320, "min=1")}${detectionInput("Corner padding", "cornerSearchPadding", 40, "min=0")}</div>`;
    const borderTolerances = `<div class="configuration-field-grid three">${detectionInput("Track depth tolerance", "trackDepthTolerance", 6, "min=0")}${detectionInput("Corner-line tolerance", "cornerLineTolerance", 16, "min=0")}${detectionInput("Maximum depth spread", "maximumDepthSpread", 24, "min=0")}</div>`;
    const borderAcceptance = `<div class="configuration-field-grid four">${detectionInput("Segment count", "segmentCount", 8, "min=1")}${detectionInput("Corner exclusion ratio", "cornerExclusionRatio", .10, "min=0 max=1 step=0.01")}${detectionInput("Compatible corners", "minimumCompatibleCorners", 3, "min=1 max=4")}${detectionInput("Minimum segment support", "minimumSegmentSupportRatio", .35, "min=0 max=1 step=0.01")}${detectionInput("Minimum side support", "minimumSideSupportRatio", .55, "min=0 max=1 step=0.01")}${detectionInput("Minimum span", "minimumSpanRatio", .70, "min=0 max=1 step=0.01")}${detectionInput("Supported segments", "minimumSupportedSegments", 6, "min=1")}${detectionInput("Missing segment run", "maximumMissingSegmentRun", 2, "min=0")}</div>`;
    const s3 = valueFor(settings, "s3Storage", {});
    const storageConfiguration = valueFor(state.storageSnapshot, "configuration", {});
    const credentialStateValue = valueFor(storageConfiguration, "credentialStatus", "NotConfigured");
    const credentialState = typeof credentialStateValue === "number" ? ["NotConfigured", "Configured", "Unavailable"][credentialStateValue] : String(credentialStateValue);
    const credentialHint = String(valueFor(storageConfiguration, "maskedAccessKey", "") ?? "").split("|")[0];
    const s3Storage = `<div class="configuration-field-grid three"><label class="field"><span>Region</span><input class="control" data-setting-text-group="s3Storage" data-setting="region" value="${escapeHtml(valueFor(s3, "region", "us-east-1"))}" autocomplete="off"></label><label class="field"><span>Bucket</span><input class="control" data-setting-text-group="s3Storage" data-setting="bucket" value="${escapeHtml(valueFor(s3, "bucket", ""))}" autocomplete="off"></label><label class="field"><span>Folder</span><input class="control" data-setting-text-group="s3Storage" data-setting="folder" value="${escapeHtml(valueFor(s3, "folder", "coloring"))}" autocomplete="off"></label></div><div class="configuration-field-grid two s3-credential-row"><label class="field"><span>Access Key</span><input class="control" data-s3-access-key type="password" autocomplete="off" placeholder="${credentialState === "Configured" ? credentialHint || "Saved" : "Required"}"></label><label class="field"><span>Secret Key</span><input class="control" data-s3-secret-key type="password" autocomplete="new-password" placeholder="${credentialState === "Configured" ? "Saved" : "Required"}"></label></div><div class="book-settings-inline"><small data-s3-credential-status>${escapeHtml(credentialState === "Configured" ? `Credentials configured${credentialHint ? ` · ${credentialHint}` : ""}` : credentialState === "Unavailable" ? "Saved credentials cannot be read. Replace them." : "Credentials are not configured.")}</small><button class="button-secondary" type="button" data-action="replace-s3-credentials" ${state.storageSettingsPending ? "disabled" : ""}>${state.storageSettingsPending ? "Replacing…" : "Replace credentials"}</button></div>`;
    content.innerHTML = `<section class="configuration-page"><div class="page-header"><div><h1>Configuration</h1><p>Manage global application settings.</p></div></div><form class="panel configuration-panel" data-form="configuration"><header class="configuration-panel-header"><div><p class="eyebrow">Settings</p><h2>Application configuration</h2><p>Shared defaults for keyword building, Amazon research, artwork preparation, final Interior output, and S3 publishing.</p></div><div class="configuration-panel-actions"><span class="configuration-save-status" data-settings-feedback data-state="${feedbackState}" role="${state.settingsFeedbackError ? "alert" : "status"}" aria-live="polite">${escapeHtml(feedback)}</span>${refreshAction("Load", state.settingsSavePending)}<button class="button-primary" type="submit" data-settings-save aria-busy="${state.settingsSavePending}" ${state.settingsSavePending ? "disabled" : ""}>${state.settingsSavePending ? "Saving…" : "Save"}</button></div></header><div class="configuration-panel-scroll"><div class="configuration-group-grid">${group("configuration-s3", "S3 Storage", "Configuration is saved with the app. Credentials are encrypted for the current Windows user and replaced separately.", s3Storage, true)}${group("configuration-runtime", "Processing capacity", "Controls the number of pages processed in parallel.", runtime)}${group("configuration-artwork-preparation", "Artwork preparation", "Normalizes source artwork before border detection and page composition.", artworkPreparation)}${group("configuration-keywords", "Keyword Builder defaults", "Shared phrases applied before each Book's own keywords.", keywordDefaults, true)}${group("configuration-amazon", "Amazon market profiles", "Browser profile, marketplace URL, locale, and title matching terms are configured independently for each Book Language.", amazonMarkets, true)}${group("configuration-working-canvas", "Working canvas", "The processing canvas must be at least as large as the maximum artwork side.", workingCanvas)}${group("configuration-final-output", "Final Interior output", "The exported raster must be at least as large as the working canvas.", finalOutput)}${group("configuration-border-range", "Border search range", "Pass 2 must include Pass 1 and remain within half of the normalized source.", borderRange)}${group("configuration-border-tolerances", "Border tolerances", "Controls how much depth and corner variation a detected frame may contain.", borderTolerances)}${group("configuration-border-acceptance", "Border acceptance rules", "Defines the segment, corner, support, and span evidence required to accept a frame.", borderAcceptance, true)}</div></div></form></section>`;
  };
  const updateSettingsSaveUi = () => {
    document.querySelectorAll('[data-setting], [data-generic-keywords], [data-generic-keyword-language], [data-amazon-profile-field], [data-amazon-market-language]').forEach((input) => { input.disabled = state.settingsSavePending; });
    const save = content.querySelector("[data-settings-save]");
    if (save) {
      save.disabled = state.settingsSavePending;
      save.textContent = state.settingsSavePending ? "Saving…" : "Save";
      save.setAttribute("aria-busy", String(state.settingsSavePending));
    }
    const load = content.querySelector('[data-action="refresh"]');
    if (load) load.disabled = state.settingsSavePending || applicationIsLoading();
    const feedback = content.querySelector("[data-settings-feedback]");
    if (feedback) {
      const message = state.settingsFeedback || "Ready";
      feedback.textContent = message;
      feedback.dataset.state = state.settingsFeedbackError ? "error" : state.settingsSavePending ? "saving" : message === "Saved" ? "saved" : "ready";
      feedback.setAttribute("role", state.settingsFeedbackError ? "alert" : "status");
    }
  };
  const beginSettingsSave = () => {
    if (state.settingsSavePending) return;
    const payload = {};
    document.querySelectorAll("[data-setting]").forEach((input) => {
      const group = input.dataset.settingGroup;
      const textGroup = input.dataset.settingTextGroup;
      if (textGroup) { payload[textGroup] ??= {}; payload[textGroup][input.dataset.setting] = String(input.value).trim(); }
      else if (group) { payload[group] ??= {}; payload[group][input.dataset.setting] = Number(input.value); }
      else payload[input.dataset.setting] = Number(input.value);
    });
    storeGenericKeywordEditorDraft();
    payload.genericKeywordsByLanguage = Object.fromEntries(supportedLanguages().map((language) => {
      const code = String(valueFor(language, "code", "")).toLocaleLowerCase();
      return [code, normalizeKeywordPhrases(state.settingsGenericKeywordDrafts.get(code) ?? "")];
    }));
    payload.genericKeywords = payload.genericKeywordsByLanguage.en ?? [];
    storeAmazonMarketplaceEditorDraft();
    payload.amazonMarketplaceProfiles = Object.fromEntries(supportedLanguages().map((language) => {
      const code = String(valueFor(language, "code", "")).toLocaleLowerCase();
      const profile = state.settingsAmazonProfileDrafts.get(code) ?? {};
      return [code, {
        profileKey: String(valueFor(profile, "profileKey", "") ?? ""),
        baseUrl: String(valueFor(profile, "baseUrl", "") ?? ""),
        locale: String(valueFor(profile, "locale", "") ?? ""),
        titleTerms: String(valueFor(profile, "titleTerms", "") ?? "")
      }];
    }));
    state.settingsSavePending = true;
    state.settingsFeedback = "Saving…";
    state.settingsFeedbackError = false;
    updateSettingsSaveUi();
    send("settings.save", payload);
  };

  const storageFileStateName = (value) => typeof value === "number" ? ["Pending", "MissingLocal", "MissingRemote", "Synced", "SyncedButNotPublic", "Changed", "Uploaded", "Skipped", "Failed", "Unknown"][value] ?? "Pending" : String(value ?? "Pending");
  const storageOutcomeName = (value) => typeof value === "number" ? ["Pending", "Running", "Completed", "CompletedWithErrors", "Cancelled", "Interrupted"][value] ?? "Pending" : String(value ?? "Pending");
  const storageActionName = (value) => typeof value === "number" ? ["Check", "Upload"][value] ?? "Check" : String(value ?? "Check");
  const storageSize = (value) => {
    const bytes = Number(value);
    if (!Number.isFinite(bytes) || bytes < 0) return "—";
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };
  const storageHash = (value) => {
    const hash = String(value ?? "");
    return hash ? `${hash.slice(0, 10)}…` : "—";
  };
  const storageErrorMessage = (code) => ({
    s3_credentials_required: "Enter both Access Key and Secret Key before saving.",
    s3_credentials_incomplete: "Enter both Access Key and Secret Key together.",
    s3_bucket_invalid: "Enter a valid S3 bucket name.",
    s3_region_invalid: "Enter a valid AWS region.",
    s3_folder_invalid: "Enter a valid non-empty S3 folder without dot segments.",
    s3_settings_required: "Save S3 configuration and credentials before checking or uploading.",
    s3_credentials_unavailable: "Stored S3 credentials cannot be read. Replace them.",
    s3_asin_invalid: "This Book needs a 10-character alphanumeric ASIN.",
    s3_asin_duplicate: "Another Book uses the same ASIN. Assign a unique ASIN before publishing.",
    s3_operation_active: "Another Book is already checking or publishing to S3.",
    book_output_busy: "Production is replacing this Book's output. Retry shortly.",
    s3_staging_insufficient_space: "There is not enough disk space to prepare the upload package.",
    s3_operation_cancelled: "The S3 operation was cancelled before publication changed.",
    s3_partial_publication: "Publication stopped after uploads began. Retry Upload to reconcile all seven files.",
    s3_upload_preflight_failed: "Upload did not start because the complete seven-file package is not available.",
    s3_publication_interrupted: "The previous S3 operation was interrupted. Run Check before retrying Upload.",
    s3_public_acl_unsupported: "This bucket rejects public-read ACLs. Choose a compatible bucket or update Object Ownership settings.",
    s3_bucket_not_found: "The configured bucket does not exist.",
    s3_region_mismatch: "The bucket is in a different AWS region.",
    s3_access_denied: "S3 denied access. Check credentials, bucket policy, and public-read permissions.",
    s3_credentials_invalid: "S3 rejected the saved credentials.",
    s3_service_unavailable: "S3 could not be reached. Check the network and configured region.",
    s3_request_failed: "The S3 request failed. Review the bucket and endpoint settings.",
    s3_receipt_unavailable: "The previous S3 receipt cannot be read. Run Check to create a fresh receipt.",
    s3_remote_verification_failed: "The uploaded object did not match the local file. Retry Upload.",
    s3_public_verification_failed: "The object exists but is not publicly reachable. Review bucket public-access settings.",
    s3_local_file_missing: "This required output file is missing. Run the named Production action.",
    s3_local_file_unavailable: "This output file could not be copied. Close programs using it and retry.",
    book_not_found: "This Book is no longer available. Refresh the library."
  })[String(code)] ?? "The S3 request failed. Review the settings and retry.";
  const storageTone = (value) => {
    const name = storageFileStateName(value);
    if (["Synced", "Uploaded", "Skipped"].includes(name)) return "status-good";
    if (["MissingLocal", "MissingRemote", "SyncedButNotPublic", "Changed", "Unknown"].includes(name)) return "status-warn";
    if (name === "Failed") return "status-bad";
    return "status-muted";
  };
  const storageSessionView = (book) => valueFor(valueFor(book, "session", {}), "view", null);
  const storageSessionActive = (book) => Boolean(valueFor(valueFor(book, "session", {}), "isActive", false));
  const storageReplaceSession = (session) => {
    const booksValue = valueFor(state.storageSnapshot, "books", []);
    const id = String(valueFor(session, "bookId", ""));
    const book = booksValue.find((item) => String(valueFor(item, "bookId", "")) === id);
    if (book) {
      if (Object.hasOwn(book, "Session") && !Object.hasOwn(book, "session")) book.Session = session;
      else book.session = session;
    }
  };
  const patchBookS3 = (id) => {
    if (currentRoute() !== "books" || !state.bookDrawerOpen || state.selectedBookTab !== "settings" || state.selectedBookId !== id) return;
    const book = selectedBook();
    const group = document.querySelector("[data-book-s3]");
    if (book && group) {
      const focusedAction = group.contains(document.activeElement) ? document.activeElement?.dataset?.action ?? "" : "";
      group.outerHTML = renderBookS3Storage(book);
      if (focusedAction) document.querySelector(`[data-book-s3] [data-action="${focusedAction}"]`)?.focus();
    }
  };
  const stopStoragePoll = (bookIdValue) => {
    const timer = state.storagePollTimers.get(bookIdValue);
    if (timer) window.clearTimeout(timer);
    state.storagePollTimers.delete(bookIdValue);
  };
  const observeStorageSession = (session) => {
    const id = String(valueFor(session, "bookId", ""));
    if (!id) return;
    storageReplaceSession(session);
    state.storagePendingBooks.delete(id);
    stopStoragePoll(id);
    if (valueFor(session, "isActive", false)) {
      state.storagePollTimers.set(id, window.setTimeout(() => send("book.s3.get", { bookId: id }), 600));
    } else {
      state.storageLoading = false;
      window.setTimeout(loadStorage, 0);
    }
    patchBookS3(id);
  };
  const loadStorage = () => {
    if (state.storageLoading) return;
    state.storageLoading = true;
    send("s3.get");
  };
  const renderStorageFileRows = (book) => {
    const view = storageSessionView(book);
    const sessionError = String(valueFor(valueFor(book, "session", {}), "errorCode", "") ?? "");
    const files = valueFor(view, "files", []);
    if (!files.length) return `<div class="storage-file-empty">Run Check or Upload to inspect the seven required output files.</div>`;
    return `<div class="storage-file-list">${files.map((file) => {
      const fileState = storageFileStateName(valueFor(file, "state", "Pending"));
      const url = String(valueFor(file, "publicUrl", ""));
      const error = String(valueFor(file, "errorCode", "") ?? "");
      const name = String(valueFor(file, "fileName", ""));
      const actions = url ? `<span class="storage-file-actions"><button class="button-secondary" type="button" data-action="storage-open-url" data-storage-url="${escapeHtml(url)}" aria-label="Open public S3 URL for ${escapeHtml(name)}">Open</button><button class="button-secondary" type="button" data-action="storage-copy-url" data-storage-url="${escapeHtml(url)}" aria-label="Copy public S3 URL for ${escapeHtml(name)}">Copy URL</button></span>` : "";
      const localExists = valueFor(file, "localExists", null);
      const remoteExists = valueFor(file, "remoteExists", null);
      const publicState = valueFor(file, "isPublic", null);
      const localFact = localExists === false ? "Missing" : localExists === true ? `${storageSize(valueFor(file, "localLength", null))} · ${storageHash(valueFor(file, "localSha256", ""))}` : "Not checked";
      const remoteFact = remoteExists === false ? "Missing" : remoteExists === true ? `${storageSize(valueFor(file, "remoteLength", null))} · ${storageHash(valueFor(file, "remoteSha256", ""))}` : "Not checked";
      const publicFact = publicState === true ? "Reachable" : publicState === false ? "Not public" : "Not checked";
      return `<div class="storage-file-row"><div class="storage-file-name"><strong>${escapeHtml(name)}</strong>${error ? `<small>${escapeHtml(storageErrorMessage(error))}</small>` : ""}</div><dl class="storage-file-facts"><div><dt>Local</dt><dd>${escapeHtml(localFact)}</dd></div><div><dt>Remote</dt><dd>${escapeHtml(remoteFact)}</dd></div><div><dt>Public</dt><dd>${escapeHtml(publicFact)}</dd></div></dl><div class="storage-file-state"><span class="status-badge ${storageTone(fileState)}">${escapeHtml(fileState)}</span>${actions}</div></div>`;
    }).join("")}</div>`;
  };
  const renderStorageBook = (book, configured) => {
    const id = String(valueFor(book, "bookId", ""));
    const active = storageSessionActive(book);
    const activeBookId = String(valueFor(state.storageSnapshot, "activeBookId", "") ?? "");
    const blockedByOtherBook = Boolean(activeBookId && activeBookId !== id);
    const pending = state.storagePendingBooks.has(id);
    const view = storageSessionView(book);
    const completed = Number(valueFor(view, "completedCount", 0));
    const total = Number(valueFor(view, "totalCount", 7)) || 7;
    const outcome = storageOutcomeName(valueFor(view, "outcome", "Pending"));
    const action = storageActionName(valueFor(view, "action", "Check"));
    const phase = String(valueFor(view, "phase", "idle") ?? "idle");
    const missing = valueFor(book, "missingFiles", []);
    const missingArtifacts = valueFor(book, "missingArtifacts", []).length
      ? valueFor(book, "missingArtifacts", [])
      : missing.map((fileName) => ({ fileName, recoveryAction: "Run the matching Production action" }));
    const asinValid = Boolean(valueFor(book, "isAsinValid", false));
    const checkDisabled = !configured || !asinValid || active || blockedByOtherBook || pending;
    const uploadDisabled = checkDisabled || missing.length > 0;
    const sessionError = String(valueFor(valueFor(book, "session", {}), "errorCode", "") ?? "");
    const warningCode = String(valueFor(view, "warningCode", "") ?? "");
    const reason = !configured ? "Save S3 configuration and replace credentials in Configuration." : !asinValid ? "Save a valid 10-character ASIN in Book Information." : blockedByOtherBook ? `${activeBookId} currently owns the S3 operation.` : missing.length ? `${missing.length} required output file${missing.length === 1 ? " is" : "s are"} missing. Check remains available; Upload is blocked.` : "Ready to check or upload the seven-file publication package.";
    const isPublishing = phase === "publishing";
    const phaseCompleted = isPublishing ? Number(valueFor(view, "uploadCompletedCount", 0)) : completed;
    const phaseTotal = isPublishing ? Number(valueFor(view, "uploadTotalCount", 0)) : total;
    const progress = phaseTotal > 0 ? Math.round((phaseCompleted / phaseTotal) * 100) : 0;
    const missingRecovery = missingArtifacts.length ? `<ul class="storage-missing-list" aria-label="Missing publication files">${missingArtifacts.map((artifact) => `<li><strong>${escapeHtml(valueFor(artifact, "fileName", "Required output"))}</strong><span>${escapeHtml(valueFor(artifact, "recoveryAction", "Run Production"))}</span></li>`).join("")}</ul>` : "";
    const blockedOwner = blockedByOtherBook ? `<div class="storage-active-owner" role="status"><strong>${escapeHtml(activeBookId)} owns the active S3 operation</strong><span>Open that Book's Settings to view live progress or stop the operation.</span></div>` : "";
    const progressMarkup = !blockedByOtherBook && view ? `<div class="storage-progress-copy" role="status" aria-live="polite"><span>${escapeHtml(action)} · ${escapeHtml(outcome)} · ${escapeHtml(phase)}</span><strong>${phaseCompleted}/${phaseTotal}</strong></div><div class="storage-progress" role="progressbar" aria-label="${escapeHtml(action)} progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${progress}"><span style="width:${progress}%"></span></div><div class="storage-time-row"><span>Checked ${dateTime(valueFor(view, "lastCheckedAtUtc", null))}</span><span>Uploaded ${dateTime(valueFor(view, "lastUploadedAtUtc", null))}</span></div>` : "";
    const filesMarkup = blockedByOtherBook ? blockedOwner : `${missingRecovery}${renderStorageFileRows(book)}`;
    return `<fieldset class="asset-background-setting book-settings-card book-settings-s3 storage-book-card" data-book-s3 data-storage-book="${escapeHtml(id)}"><legend>S3 Storage</legend><div class="storage-settings-heading"><p>${escapeHtml(valueFor(book, "title", id))} · ASIN ${escapeHtml(valueFor(book, "asin", "Not set") || "Not set")}</p>${badge(active ? `${action} · ${phase}` : blockedByOtherBook ? "Blocked" : missing.length ? "Check available" : "Ready")}</div><p class="storage-book-reason">${escapeHtml(reason)}</p>${sessionError && !blockedByOtherBook ? `<p class="storage-book-error" role="alert">${escapeHtml(storageErrorMessage(sessionError))}</p>` : ""}${warningCode && !blockedByOtherBook ? `<p class="storage-book-error" role="alert">${escapeHtml(storageErrorMessage(warningCode))}</p>` : ""}${progressMarkup}${filesMarkup}<footer><button class="button-secondary" type="button" data-action="storage-check" data-book-id="${escapeHtml(id)}" ${checkDisabled ? "disabled" : ""} title="${escapeHtml(reason)}">Check</button><button class="button-primary" type="button" data-action="storage-upload" data-book-id="${escapeHtml(id)}" ${uploadDisabled ? "disabled" : ""} title="${escapeHtml(reason)}">Upload</button>${active ? `<button class="button-danger" type="button" data-action="storage-cancel" data-book-id="${escapeHtml(id)}">Stop publishing</button>` : ""}</footer></fieldset>`;
  };

  const renderBookS3Storage = (book) => {
    const overview = valueFor(state.storageSnapshot, "books", []).find((item) => String(valueFor(item, "bookId", "")) === bookId(book));
    if (!overview) return `<fieldset class="asset-background-setting book-settings-card book-settings-s3 storage-book-card" data-book-s3><legend>S3 Storage</legend><p class="storage-book-reason" role="status">Loading S3 readiness…</p></fieldset>`;
    const configuration = valueFor(state.storageSnapshot, "configuration", {});
    const credentialState = valueFor(configuration, "credentialStatus", "NotConfigured");
    const destination = valueFor(configuration, "configuration", {});
    const destinationReady = ["region", "bucket", "folder"].every((name) => String(valueFor(destination, name, "") ?? "").trim().length > 0);
    const configured = (credentialState === "Configured" || credentialState === 1) && destinationReady;
    return renderStorageBook(overview, configured);
  };
  const brandMetadataPresentation = (brand) => {
    const summary = brandSummaryFor(brand);
    const metadataStatus = valueFor(summary, "metadataStatus", "Missing");
    if (metadataStatus === "Unavailable") return { label: "Metadata unavailable", tone: "status-bad" };
    if (brandAuthor(brand)) return { label: "Author ready", tone: "status-good" };
    return { label: "Author missing", tone: "status-bad" };
  };
  const brandListMarkup = (brands) => brands.length
    ? brands.map((brand) => {
      const name = valueFor(brand, "name", "");
      const selected = name === state.inspectedBrand;
      const metadata = brandMetadataPresentation(brand);
      return `<button class="brand-row ${selected ? "brand-row-active" : ""}" type="button" data-action="select-brand" data-brand-name="${escapeHtml(name)}" aria-pressed="${selected}"><span class="brand-row-copy"><strong title="${escapeHtml(name)}">${escapeHtml(name)}</strong><small>${escapeHtml(brandAuthor(brand) || "Primary Author not set")}</small></span><span class="brand-row-badges"><span class="status-badge ${metadata.tone}">${escapeHtml(metadata.label)}</span>${badge(brandValidationStatus(valueFor(brandSummaryFor(brand), "validationStatus", "NotValidated")))}</span></button>`;
    }).join("")
    : "<div class=\"brand-list-empty\"><strong>No matching Brands</strong><p>Try a different Brand name.</p></div>";
  const refreshBrandList = () => {
    const search = state.brandFilter.trim().toLocaleLowerCase();
    const availableBrands = valueFor(discovery(), "brands", []);
    const brands = availableBrands.filter((brand) => !search || valueFor(brand, "name", "").toLocaleLowerCase().includes(search));
    const list = content.querySelector("[data-brand-list]");
    if (list) list.innerHTML = brandListMarkup(brands);
    const count = content.querySelector("[data-brand-result-count]");
    if (count) count.textContent = `${brands.length} of ${availableBrands.length} shown`;
  };

  const renderBrands = () => {
    const availableBrands = valueFor(discovery(), "brands", []);
    if (!availableBrands.some((brand) => valueFor(brand, "name", "") === state.inspectedBrand)) state.inspectedBrand = valueFor(availableBrands[0], "name", "");
    const selected = availableBrands.find((brand) => valueFor(brand, "name", "") === state.inspectedBrand);
    const search = state.brandFilter.trim().toLocaleLowerCase();
    const allBrands = availableBrands.filter((brand) => !search || valueFor(brand, "name", "").toLocaleLowerCase().includes(search));
    const assets = valueFor(selected, "assets", []);
    const selectedValidation = brandSummaryFor(selected);
    const validationStatus = brandValidationStatus(valueFor(selectedValidation, "validationStatus", "NotValidated"));
    const validationResult = valueFor(state.brandValidationResult, "brandName", "") === state.inspectedBrand ? state.brandValidationResult : null;
    const validationFailures = valueFor(validationResult, "failures", []);
    const dimensions = (value) => {
      const width = valueFor(value, "width", 0);
      const height = valueFor(value, "height", 0);
      return width && height ? `${width} × ${height} px` : "—";
    };
    const size = (asset) => dimensions(valueFor(asset, "size", null));
    const targetFor = (asset, entry = null) => entry ? `${valueFor(asset, "name", "")}/${valueFor(entry, "name", "")}` : valueFor(asset, "name", "");
    const failureFor = (asset, entry = null) => validationFailures.find((failure) => String(valueFor(failure, "target", "")).replaceAll("\\", "/").toLocaleLowerCase() === targetFor(asset, entry).toLocaleLowerCase());
    const statusFor = (asset, entry = null) => {
      if (failureFor(asset, entry)) return "Needs attention";
      if (!entry && validationFailures.some((failure) => String(valueFor(failure, "target", "")).replaceAll("\\", "/").toLocaleLowerCase().startsWith(`${targetFor(asset).toLocaleLowerCase()}/`))) return "Needs attention";
      return valueFor(entry ?? asset, "status", "Missing");
    };
    const expectedSize = (asset) => {
      const name = valueFor(asset, "name", "");
      const requirement = valueFor(window.appSnapshot, "brandImageSizeRequirements", [])
        .find((item) => valueFor(item, "target", "") === name);
      const allowedSizes = valueFor(requirement, "allowedSizes", []);
      if (allowedSizes.length) return allowedSizes.map(dimensions).join(", ");

      // The desktop may show a cached snapshot while its host finishes loading.
      // These two file contracts are direct projections of global settings.
      const settings = valueFor(window.appSnapshot, "globalSettings", {});
      if (name === "frame.png") {
        const maximumSide = valueFor(settings, "artworkMaximumSide", null);
        return maximumSide ? `${maximumSide} × ${maximumSide} px` : "—";
      }
      if (name === "background.png") {
        const width = valueFor(settings, "finalPageWidth", null);
        const height = valueFor(settings, "finalPageHeight", null);
        return width && height ? `${width} × ${height} px` : "—";
      }
      return "Validate Brand to confirm the required size";
    };
    const validationMessage = validationResult && !valueFor(validationResult, "isSuccess", false)
      ? `<section class="brand-validation-summary" role="alert" tabindex="-1" data-brand-validation-summary aria-labelledby="brand-validation-title"><h3 id="brand-validation-title">Fix these Brand assets</h3><p>Each item names the file, its current size, and the size required before processing.</p><ul>${validationFailures.map((failure) => `<li><strong>${escapeHtml(valueFor(failure, "target", "Brand asset"))}</strong><span>${escapeHtml(valueFor(failure, "message", "Validation failed."))}</span></li>`).join("")}</ul></section>`
      : "";
    const renderFolder = (asset) => {
      const entries = valueFor(asset, "entries", []);
      return `<section class="brand-folder"><div class="brand-asset-heading"><div><h3>${escapeHtml(valueFor(asset, "name", ""))}</h3><p>${escapeHtml(valueFor(asset, "type", "Folder"))} · ${entries.length} file${entries.length === 1 ? "" : "s"}</p></div>${badge(statusFor(asset))}</div><table class="data-table brand-folder-table"><thead><tr><th>Name</th><th>Extension</th><th>Size</th><th>Status</th></tr></thead><tbody>${entries.length ? entries.map((entry) => { const failure = failureFor(asset, entry); return `<tr><td>${escapeHtml(valueFor(entry, "name", ""))}${failure ? `<small class="brand-asset-error">${escapeHtml(valueFor(failure, "message", "Validation failed."))}</small>` : ""}</td><td>${escapeHtml(valueFor(entry, "extension", "—") || "—")}</td><td>${escapeHtml(size(entry))}</td><td>${badge(statusFor(asset, entry))}</td></tr>`; }).join("") : "<tr><td colspan=\"4\" class=\"empty-copy\">No files found.</td></tr>"}</tbody></table>${valueFor(asset, "name", "") === "IntroTemplate" ? `<p class="brand-size-note">Required image size: ${expectedSize(asset)}.</p>` : ""}</section>`;
    };
    const renderFile = (asset) => {
      const failure = failureFor(asset);
      const isImage = valueFor(asset, "type", "") === "Image";
      const details = isImage ? `<dl><div><dt>Current size</dt><dd>${escapeHtml(size(asset))}</dd></div><div><dt>Required size</dt><dd>${escapeHtml(expectedSize(asset))}</dd></div></dl>` : "";
      return `<article class="brand-file-card"><div class="brand-asset-heading"><div><h3>${escapeHtml(valueFor(asset, "name", ""))}</h3><p>${isImage ? escapeHtml(valueFor(asset, "extension", "") || "Image") : "Required PSD template"}</p></div>${badge(statusFor(asset))}</div>${details}${failure ? `<p class="brand-asset-error">${escapeHtml(valueFor(failure, "message", "Validation failed."))}</p>` : ""}</article>`;
    };
    const folders = assets.filter((asset) => valueFor(asset, "type", "") === "Folder");
    const files = assets.filter((asset) => valueFor(asset, "type", "") !== "Folder");
    const assetInventory = assets.length ? `<section class="brand-template-section"><div class="brand-section-heading"><div><h3>Template inventory</h3><p>Review the discovered folders and required production files for this Brand.</p></div><span>${assets.length} asset${assets.length === 1 ? "" : "s"}</span></div><div class="brand-asset-inventory"><div class="brand-folder-list">${folders.map(renderFolder).join("")}</div><div class="brand-file-grid">${files.map(renderFile).join("")}</div></div></section>` : "<section class=\"brand-template-section\"><div class=\"brand-list-empty\"><strong>No Brand assets found</strong><p>Add the required folders and template files, then refresh the library.</p></div></section>";
    const authorDraft = selected ? brandAuthorDraftFor(selected) : "";
    const authorDirty = selected ? brandAuthorIsDirty(selected, authorDraft) : false;
    const impactedBooks = selected && authorDirty ? books().filter((book) => {
      const summary = summaryFor(book);
      return assignedBrandName(summary) === valueFor(selected, "name", "") && !authorMatches(valueFor(metadataFor(summary), "author", ""), authorDraft);
    }).length : 0;
    const metadata = selected ? brandMetadataPresentation(selected) : null;
    const sourceBrandName = selected ? String(valueFor(selected, "name", "")) : "";
    const cloneDestination = brandCloneDestinationName(sourceBrandName, state.brandCloneLanguageCode);
    const cloneDestinationExists = Boolean(cloneDestination) && availableBrands.some((brand) => String(valueFor(brand, "name", "")).toLocaleLowerCase() === cloneDestination.toLocaleLowerCase());
    const cloneBusy = state.brandClonePending || state.brandCloneAwaitingSnapshot;
    const cloneBlocked = !state.brandCloneLanguageCode || !cloneDestination || cloneDestinationExists || cloneBusy || processIsActive();
    const cloneFeedback = cloneDestinationExists && !state.brandCloneFeedback
      ? "That destination Brand already exists. Select another language."
      : state.brandCloneFeedback;
    const cloneFeedbackError = cloneDestinationExists || state.brandCloneFeedbackError;
    const cloneGroup = selected && state.brandCloneOpen ? `<fieldset class="brand-clone-group" aria-busy="${cloneBusy}" ${cloneBusy ? "disabled" : ""}><legend>Clone Brand</legend><div class="brand-clone-grid"><label class="field"><span>Source Brand</span><input class="control" value="${escapeHtml(sourceBrandName)}" readonly aria-readonly="true"></label><label class="field"><span>Language</span><select class="control" data-action="clone-brand-language"><option value="">Select language</option>${supportedLanguages().map((option) => { const code = String(valueFor(option, "code", "")); return `<option value="${escapeHtml(code)}" ${code === state.brandCloneLanguageCode ? "selected" : ""}>${escapeHtml(valueFor(option, "name", code))}</option>`; }).join("")}</select></label><label class="field"><span>Destination Brand name</span><input class="control" value="${escapeHtml(cloneDestination)}" placeholder="Select a language" readonly aria-readonly="true"></label></div><div class="brand-clone-footer"><p class="brand-clone-feedback ${cloneFeedbackError ? "is-error" : ""}" role="${cloneFeedbackError ? "alert" : "status"}" aria-live="polite">${escapeHtml(cloneFeedback)}</p><div class="brand-clone-actions"><button class="button-secondary" type="button" data-action="cancel-brand-clone">Cancel</button><button class="button-primary" type="button" data-action="submit-brand-clone" aria-busy="${cloneBusy}" ${cloneBlocked ? "disabled" : ""}>${cloneBusy ? "Cloning…" : "Clone Brand"}</button></div></div></fieldset>` : "";
    const cloneNotice = selected && state.brandCloneNotice ? `<p class="brand-clone-notice" role="status" aria-live="polite">${escapeHtml(state.brandCloneNotice)}</p>` : "";
    const brandInfo = selected ? `<section class="catalog-card brand-region-card"><div class="catalog-card-heading"><div><h3>Brand Information</h3><p>One Brand has one Primary Author and one persisted Language.</p></div><span class="status-badge ${metadata.tone}">${escapeHtml(metadata.label)}</span></div><label class="field"><span>Language</span><input class="control" value="${escapeHtml(languageNameFor(brandSummaryFor(selected)))}" readonly aria-readonly="true"></label><label class="field"><span>Author</span><input class="control" data-action="brand-author-input" data-brand-name="${escapeHtml(valueFor(selected, "name", ""))}" value="${escapeHtml(authorDraft)}" placeholder="Unknown" autocomplete="off"></label>${impactedBooks ? `<p class="catalog-warning" role="alert">Saving this Author will make ${impactedBooks} assigned Book${impactedBooks === 1 ? "" : "s"} invalid. Their assignments will be kept for review.</p>` : ""}<div class="catalog-actions"><p class="catalog-feedback ${state.catalogFeedbackError ? "is-error" : ""}" role="${state.catalogFeedbackError ? "alert" : "status"}" aria-live="polite">${state.catalogMutationTarget === valueFor(selected, "name", "") ? escapeHtml(state.catalogFeedback) : ""}</p><button class="button-primary" data-action="save-brand-author" data-brand-name="${escapeHtml(valueFor(selected, "name", ""))}" aria-busy="${state.catalogMutationPending && state.catalogMutationCommand === "brand.author.save"}" ${!authorDirty || state.catalogMutationPending || processIsActive() ? "disabled" : ""}>${state.catalogMutationPending && state.catalogMutationCommand === "brand.author.save" ? "Saving…" : "Save Author"}</button></div></section>` : "";
    const validationInfo = selected ? `<section class="catalog-card brand-region-card brand-validation-card"><div class="catalog-card-heading"><div><h3>Brand Validation</h3><p>Check every required asset before processing.</p></div>${badge(validationStatus)}</div><dl class="brand-validation-facts"><div><dt>Last validated</dt><dd>${escapeHtml(dateTime(valueFor(selectedValidation, "validatedAtUtc", null)))}</dd></div><div><dt>Files checked</dt><dd>IntroTemplate + 5 required files</dd></div></dl><p class="panel-note">Validate IntroTemplate, frame.png, background.png, cover.psd, app_plus.psd, and book_owner.psd.</p><div class="brand-validation-action"><button class="button-primary" data-action="validate-brand" ${processIsActive() ? "disabled" : ""}>Validate Brand</button></div></section>` : "";
    const detail = selected
      ? `<section class="panel brand-detail-panel"><header class="brand-panel-header"><div><p class="eyebrow">Selected Brand</p><h2>${escapeHtml(valueFor(selected, "name", ""))}</h2></div><div class="brand-detail-actions"><button class="button-secondary" type="button" data-action="open-brand-clone" aria-expanded="${state.brandCloneOpen}" ${cloneBusy || processIsActive() ? "disabled" : ""}>Clone Brand</button><div class="brand-detail-badges"><span class="status-badge ${metadata.tone}">${escapeHtml(metadata.label)}</span>${badge(validationStatus)}</div></div></header>${cloneNotice}${cloneGroup}<div class="brand-detail-scroll"><div class="brand-region-grid">${brandInfo}${validationInfo}</div>${validationMessage}${assetInventory}</div></section>`
      : `<section class="panel brand-detail-panel"><header class="brand-panel-header"><div><p class="eyebrow">Selected Brand</p><h2>Brand detail</h2></div></header><div class="brand-detail-scroll"><div class="brand-detail-empty"><strong>No Brand selected</strong><p>Add a Brand folder or refresh the library to inspect its templates.</p></div></div></section>`;
    content.innerHTML = `<section class="brands-page"><div class="page-header"><div><h1>Brands & templates</h1><p>Inspect reusable Brand assets and resolve exact file requirements before processing.</p></div></div><div class="brand-workspace"><section class="panel brand-list-panel"><header class="brand-panel-header"><div><h2>Brands</h2><p data-brand-result-count aria-live="polite">${allBrands.length} of ${availableBrands.length} shown</p></div></header><label class="brand-search"><span class="sr-only">Search Brands by name</span><input class="control" type="search" data-action="filter-brands" value="${escapeHtml(state.brandFilter)}" placeholder="Search Brand name…" autocomplete="off"></label><div class="brand-list-scroll" data-brand-list>${brandListMarkup(allBrands)}</div></section>${detail}</div></section>`;
  };

  const renderProcessedInteriorPages = (summary) => {
    const pages = [...valueFor(summary, "interiorPages", [])]
      .filter((page) => displayStatus(valueFor(page, "status", "")) === "Completed")
      .sort((left, right) => String(valueFor(left, "pageId", "")).localeCompare(String(valueFor(right, "pageId", "")), undefined, { numeric: true, sensitivity: "base" }));
    if (!pages.length) return `<section class="processed-interior-pages"><div class="processed-interior-pages-empty"><h3>No processed pages</h3><p>Process Interior to create preview pages for this Book.</p></div></section>`;
    const tile = (page, index) => {
      const pageId = valueFor(page, "pageId", `page-${index + 1}`);
      return `<figure class="processed-interior-page"><div class="processed-interior-page-preview">${localImageMarkup(page, `Processed Interior page ${index + 1}`, "Preview unavailable")}</div><figcaption><strong>Page ${index + 1}</strong><span title="${escapeHtml(pageId)}">${escapeHtml(pageId)}</span></figcaption></figure>`;
    };
    return `<section class="processed-interior-pages"><header class="processed-interior-pages-heading"><div><h3>Interior pages</h3><p>Read-only previews created by the most recent Interior Processing run.</p></div><span class="interior-artwork-count" role="status"><strong>${pages.length}</strong> processed</span></header><div class="processed-interior-pages-scroll"><div class="processed-interior-pages-grid">${pages.map(tile).join("")}</div></div></section>`;
  };

  const renderProductionImage = (url, alt, fallback, className = "") => url
    ? `<img class="${className}" src="${escapeHtml(url)}" alt="${escapeHtml(alt)}" loading="lazy" decoding="async" data-local-image data-image-fallback="${escapeHtml(fallback)}">`
    : `<span class="book-preview-fallback" aria-label="${escapeHtml(fallback)}">${escapeHtml(fallback)}</span>`;

  const productionPdfNameErrorMessage = (code) => ({
    pdf_name_source_missing: "PDF filename sources are missing. Restore Metadata/cover_key.txt and Metadata/interior_key.txt, then restart the app.",
    pdf_name_source_unreadable: "PDF filename sources could not be read. Check file permissions, then restart the app.",
    pdf_name_source_empty: "PDF filename sources do not contain valid Windows filenames. Update them, then restart the app.",
    book_not_found: "This Book is no longer available. Refresh the library.",
    snapshot_unavailable: "The library snapshot is unavailable. Refresh the library and retry."
  })[String(code)] ?? "PDF filename suggestions are unavailable. Check the Metadata files and retry.";

  const requestProductionPdfNames = (id, regenerate = false) => {
    if (!id || state.productionPdfNamePending.has(id) || (!regenerate && state.productionPdfNames.has(id))) return;
    state.productionPdfNamePending.add(id);
    state.productionPdfNameErrors.delete(id);
    const requestId = send("book.production.pdf-name-suggestions.get", { bookId: id, regenerate });
    state.productionPdfNameRequests.set(requestId, { bookId: id, regenerate });
  };

  const renderProductionWorkspace = (book, summary) => {
    const production = productionSummaryFor(summary);
    const productionAssetOrder = { "final-cover": 0, "interior-cover": 1, "book-owner": 2 };
    const assets = [...valueFor(production, "assets", [])].sort((left, right) => (productionAssetOrder[valueFor(left, "assetKind", "")] ?? 99) - (productionAssetOrder[valueFor(right, "assetKind", "")] ?? 99));
    const coverOutput = pdfLibraryOutputs(summary).find((output) => valueFor(output, "artifactKind", "") === "Cover");
    const coverPreviewUrl = valueFor(coverOutput, "thumbnailImageUrl", "");
    const taskBusy = productionActionActive();
    const controlsBusy = taskBusy || processIsActive() || state.cacheCleanupActive || applicationIsLoading();
    const actionFor = (kind) => kind === "final-cover" ? "build-cover-pdf" : kind === "interior-cover" ? "process-interior-cover" : "process-book-owner";
    const actionLabel = (kind) => kind === "final-cover" ? "Build Cover PDF" : kind === "interior-cover" ? "Process Interior Cover" : "Process Book Owner";
    const group = (asset) => {
      const kind = valueFor(asset, "assetKind", "");
      const label = valueFor(asset, "displayName", "Production asset");
      const sourceStatus = valueFor(asset, "sourceStatus", "Missing");
      const processedStatus = valueFor(asset, "processedStatus", "Not applicable");
      const exists = sourceStatus !== "Missing";
      const importing = state.productionImportPending === kind;
      const activeAction = taskBusy && state.productionActionName === actionFor(kind);
      const sourcePreview = renderProductionImage(valueFor(asset, "sourceLocalImageUrl", ""), `${label} source`, `Upload ${label}`, "production-source-image");
      const previewUrl = kind === "final-cover" ? coverPreviewUrl : valueFor(asset, "processedLocalImageUrl", "");
      const previewFallback = kind === "final-cover" ? "Build Cover PDF to create a preview" : "Not processed";
      const processedPreview = renderProductionImage(previewUrl, `${label} preview`, previewFallback, "production-processed-image");
      const actionStatus = kind === "final-cover" ? valueFor(production, "coverOutputStatus", "Missing") : processedStatus;
      const actionDisabled = !exists || controlsBusy || importing;
      const reason = !exists ? `Upload ${label} first.` : controlsBusy ? "Another processing action is active." : `${actionLabel(kind)} from the current canonical PNG.`;
      return `<fieldset class="production-group production-asset-${kind}" data-production-asset-group="${kind}"><legend>${escapeHtml(label)}</legend><div class="production-asset-layout"><figure class="production-preview-item"><figcaption>Source image</figcaption><div class="production-preview ${kind === "final-cover" ? "production-preview-cover" : ""}">${sourcePreview}</div></figure><figure class="production-preview-item"><figcaption>Preview image</figcaption><div class="production-processed-preview ${kind === "final-cover" ? "production-preview-cover" : ""}">${processedPreview}</div></figure><aside class="production-asset-controls" aria-label="${escapeHtml(label)} controls"><div class="production-group-heading"><p title="${escapeHtml(valueFor(asset, "fileName", ""))}">${escapeHtml(valueFor(asset, "fileName", ""))}</p>${badge(sourceStatus)}</div><dl><div><dt>Output status</dt><dd>${badge(actionStatus)}</dd></div><div><dt>Last processed</dt><dd>${dateTime(kind === "final-cover" ? valueFor(production, "coverBuiltAtUtc", null) : valueFor(asset, "processedAtUtc", null))}</dd></div></dl><div class="production-card-actions"><button class="button-secondary" data-action="upload-production-asset" data-production-asset="${kind}" data-production-idle-label="${exists ? "Replace" : "Upload"}" data-book-id="${escapeHtml(bookId(book))}" ${controlsBusy || importing ? "disabled" : ""}>${importing ? "Selecting…" : exists ? "Replace" : "Upload"}</button><button class="button-primary" data-action="start-production-action" data-production-action="${actionFor(kind)}" data-production-source-exists="${exists}" data-production-idle-label="${actionLabel(kind)}" data-book-id="${escapeHtml(bookId(book))}" aria-describedby="production-help-${kind}" aria-busy="${activeAction}" ${actionDisabled ? "disabled" : ""}>${activeAction ? "Working…" : actionLabel(kind)}</button></div><p class="production-action-help" id="production-help-${kind}">${escapeHtml(reason)}${kind === "final-cover" ? " Replaces the current Cover PDF only after a successful build." : " Final Interior remains unchanged."}</p></aside></div></fieldset>`;
    };
    const readiness = productionFinalReadiness(book, summary);
    const feedbackTone = state.productionFeedbackError ? "is-error" : state.productionFeedbackWarning ? "is-warning" : "";
    const feedback = `<p class="production-feedback ${feedbackTone}" data-production-feedback role="${state.productionFeedbackError || state.productionFeedbackWarning ? "alert" : "status"}" aria-live="polite" aria-atomic="true" ${state.productionFeedback ? "" : "hidden"}>${escapeHtml(state.productionFeedback)}</p>`;
    const id = bookId(book);
    const suggestedNames = state.productionPdfNames.get(id);
    const namesPending = state.productionPdfNamePending.has(id);
    const namesError = state.productionPdfNameErrors.get(id) ?? "";
    const suggestedName = (label, inputId, value) => `<label class="field production-pdf-name-field" for="${inputId}"><span>${label}</span><input id="${inputId}" class="control" type="text" value="${escapeHtml(value)}" placeholder="${namesPending ? "Generating…" : "Unavailable"}" readonly aria-describedby="production-pdf-name-help production-pdf-name-error"></label>`;
    const nameSuggestions = `<fieldset class="production-group production-pdf-names" aria-busy="${namesPending}"><legend>Suggested PDF filenames</legend><div class="production-pdf-name-grid">${suggestedName("Cover PDF filename", "production-cover-pdf-name", valueFor(suggestedNames, "coverFileName", ""))}${suggestedName("Interior PDF filename", "production-interior-pdf-name", valueFor(suggestedNames, "interiorFileName", ""))}<button class="button-secondary" data-action="randomize-pdf-names" data-book-id="${escapeHtml(id)}" ${namesPending ? "disabled" : ""}>${namesPending ? suggestedNames ? "Randomizing…" : "Generating…" : namesError ? "Retry" : "Randomize"}</button></div><p id="production-pdf-name-help" class="production-action-help">Suggestions only. Production keeps the canonical Cover and Interior filenames; rename downloaded files when needed.</p><p id="production-pdf-name-error" class="production-pdf-name-error" role="${namesError ? "alert" : "status"}" aria-live="polite" ${namesError ? "" : "hidden"}>${escapeHtml(namesError)}</p></fieldset>`;
    const finalBusy = productionInteriorIsRunning();
    const finalBlocked = processIsActive() || state.processStartPending || applicationIsLoading();
    const finalInterior = `<fieldset class="production-group production-final-action"><legend>Final Interior</legend><div><p>Order: Interior Cover → Book Owner → Intro → randomized Interior. Background pages follow the saved HasBackground setting.</p><p id="production-final-help">${escapeHtml(readiness.reason)} Only Build Final Interior replaces the current Interior PDF. Process Interior refreshes processed-page previews only.</p><small>Current output: ${escapeHtml(valueFor(production, "interiorOutputKind", "Legacy"))} · ${dateTime(valueFor(production, "interiorBuiltAtUtc", null))}</small></div><button class="button-primary" data-action="build-final-interior" data-production-ready="${readiness.ready}" data-book-id="${escapeHtml(bookId(book))}" aria-describedby="production-final-help" aria-busy="${finalBusy}" ${readiness.ready && !finalBlocked ? "" : "disabled"}>${finalBusy ? "Building…" : "Build Final Interior"}</button></fieldset>`;
    return `<section class="production-workspace" aria-busy="${taskBusy || finalBusy}">${feedback}<div class="production-group-grid">${nameSuggestions}${finalInterior}${assets.length ? assets.map(group).join("") : "<p class=\"empty-copy production-assets-empty\">Production workspace status is unavailable. Refresh the library.</p>"}</div></section>`;
  };

  const renderBookInformation = (book, summary) => {
    const draft = metadataDraftFor(book, summary);
    const dirty = hasMetadataDraft(book, summary);
    const validation = metadataValidationFor(bookId(book));
    const assignmentWarning = metadataAssignmentWarning(draft, summary);
    const disabled = catalogMutationBusy() || processIsActive();
    const field = (label, name, placeholder = "Unknown") => {
      const errors = validation.errors.filter((error) => error.field === name);
      const errorId = `book-${name}-errors`;
      return `<label class="field" for="book-${name}-input"><span>${label}</span><input id="book-${name}-input" class="control ${name === "title" ? "book-title-input" : ""} ${errors.length ? "control-invalid" : ""}" data-action="book-metadata-input" data-metadata-field="${name}" data-book-id="${escapeHtml(bookId(book))}" value="${escapeHtml(draft[name])}" placeholder="${placeholder}" aria-describedby="${errorId}" aria-invalid="${errors.length > 0}" autocomplete="off" ${name === "title" ? 'autocapitalize="characters" spellcheck="false"' : ""}><small id="${errorId}" class="field-error" ${errors.length ? "" : "hidden"}>${errors.map((error) => escapeHtml(error.message)).join("<br>")}</small></label>`;
    };
    return `<fieldset class="catalog-card book-settings-card book-settings-information" data-book-information-card aria-busy="${state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save"}"><legend>Book Information</legend><div class="catalog-card-heading"><p>Paste production metadata from your ChatGPT Web analysis. Blank fields remain Unknown.</p>${dirty ? '<span class="status-badge status-warn">Unsaved</span>' : ""}</div><div class="catalog-form-grid"><label class="field"><span>Language</span><input class="control" value="${escapeHtml(languageNameFor(summary))}" readonly aria-readonly="true"></label>${field("Title", "title")}${field("Subtitle", "subtitle")}${field("Subcover", "subcover")}${field("ASIN", "asin")}${field("Author", "author", "Primary Author or Unknown")}<label class="field catalog-description-field"><span>Description</span><textarea class="control" rows="4" data-action="book-metadata-input" data-metadata-field="description" data-book-id="${escapeHtml(bookId(book))}" placeholder="Unknown">${escapeHtml(draft.description)}</textarea></label></div><p id="book-author-assignment-warning" class="catalog-warning" role="alert" ${assignmentWarning ? "" : "hidden"}>${escapeHtml(assignmentWarning)}</p><div class="catalog-actions"><p class="catalog-feedback ${state.catalogFeedbackError ? "is-error" : ""}" data-catalog-feedback="metadata" role="${state.catalogFeedbackError ? "alert" : "status"}">${state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.metadata.save" ? escapeHtml(state.catalogFeedback) : ""}</p><button class="button-primary" data-action="save-book-metadata" data-book-id="${escapeHtml(bookId(book))}" aria-busy="${state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save"}" ${!dirty || disabled ? "disabled" : ""}>${state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save" ? "Saving…" : "Save Book Information"}</button></div></fieldset>`;
  };

  const renderBookBrandAssignment = (book, summary) => {
    const assigned = assignedBrandName(summary);
    const currentStatus = assignmentStatus(summary);
    const languageCandidates = languageBrandsFor(summary);
    const candidates = matchingBrandsFor(summary);
    const valid = currentStatus === "Valid";
    const unavailableCurrent = assigned && !candidates.some((brand) => valueFor(brand, "name", "") === assigned);
    const options = [`<option value="">Select a matching Brand…</option>`, ...(unavailableCurrent ? [`<option value="${escapeHtml(assigned)}" selected disabled>${escapeHtml(assigned)} — ${escapeHtml(assignmentLabel(summary))}</option>`] : []), ...candidates.map((brand) => `<option value="${escapeHtml(valueFor(brand, "name", ""))}" ${valid && valueFor(brand, "name", "") === assigned ? "selected" : ""}>${escapeHtml(valueFor(brand, "name", ""))}</option>`)].join("");
    const author = valueFor(metadataFor(summary), "author", "");
    const reason = assigned && !valid ? valueFor(summary, "assignmentReason", "Choose another Brand or unassign this Book.") : "";
    const disabled = catalogMutationBusy() || processIsActive();
    const assignmentCommands = ["book.brand.assign", "book.brand.unassign"];
    const feedbackVisible = state.catalogMutationTarget === bookId(book) && assignmentCommands.includes(state.catalogMutationCommand);
    const availability = !author
      ? "Save a Book Author before assigning a Brand."
      : languageCandidates.length === 0
        ? `No ${languageNameFor(summary)} Brand available.`
        : candidates.length === 0
          ? `No ${languageNameFor(summary)} Brand matches this Book Author.`
          : `${candidates.length} matching ${languageNameFor(summary)} Brand${candidates.length === 1 ? "" : "s"}.`;
    return `<fieldset class="catalog-card book-settings-card book-settings-assignment" data-book-assignment-card><legend>Brand Assignment</legend><div class="catalog-card-heading"><p>Only Brands whose Language and Author match this Book are available.</p>${badge(assigned ? assignmentLabel(summary) : "Unassigned")}</div><dl class="catalog-assignment-summary"><div><dt>Book Language</dt><dd>${escapeHtml(languageNameFor(summary))}</dd></div><div><dt>Book Author</dt><dd>${escapeHtml(author || "Unknown")}</dd></div><div><dt>Assigned Brand</dt><dd>${escapeHtml(assigned || "Unassigned")}</dd></div></dl>${reason ? `<p class="catalog-warning" role="alert">${escapeHtml(reason)} The existing assignment is preserved until you choose what to do.</p>` : ""}<label class="field"><span>Select Brand</span><select class="control" data-action="book-brand-select" data-book-id="${escapeHtml(bookId(book))}" ${!author || disabled ? "disabled" : ""}>${options}</select><small>${escapeHtml(availability)}</small></label><div class="catalog-actions"><p class="catalog-feedback ${feedbackVisible && state.catalogFeedbackError ? "is-error" : ""}" data-catalog-feedback="assignment" role="${feedbackVisible && state.catalogFeedbackError ? "alert" : "status"}">${feedbackVisible ? escapeHtml(state.catalogFeedback) : ""}</p><div><button class="button-secondary" data-action="unassign-book-brand" data-book-id="${escapeHtml(bookId(book))}" ${!assigned || disabled ? "disabled" : ""}>Unassign</button><button class="button-primary" data-action="assign-book-brand" data-book-id="${escapeHtml(bookId(book))}" disabled>${state.catalogMutationPending && state.catalogMutationCommand === "book.brand.assign" ? "Assigning…" : "Assign Brand"}</button></div></div></fieldset>`;
  };

  const renderBookCompletion = (book, summary) => {
    const completed = bookIsCompleted(summary);
    const pending = catalogMutationBusy() && state.catalogMutationCommand === "book.completion.set" && state.catalogMutationTarget === bookId(book);
    const feedbackVisible = state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.completion.set";
    const disabled = catalogMutationBusy() || processIsActive();
    return `<fieldset class="catalog-card book-settings-card book-settings-completion" data-book-completion-card aria-busy="${pending}"><legend>Book completion</legend><div class="catalog-card-heading"><p>Mark this Book when your work is finished. This does not change its processing status.</p>${badge(completed ? "Completed" : "Not completed")}</div><div class="catalog-actions"><p class="catalog-feedback ${feedbackVisible && state.catalogFeedbackError ? "is-error" : ""}" data-catalog-feedback="completion" role="${feedbackVisible && state.catalogFeedbackError ? "alert" : "status"}" aria-live="polite">${feedbackVisible ? escapeHtml(state.catalogFeedback) : ""}</p><button class="${completed ? "button-secondary" : "button-primary"}" type="button" data-action="set-book-completion" data-book-id="${escapeHtml(bookId(book))}" data-is-completed="${!completed}" aria-busy="${pending}" ${disabled ? "disabled" : ""}>${pending ? "Saving…" : completed ? "Mark as incomplete" : "Mark as completed"}</button></div></fieldset>`;
  };

  const renderAsinResearch = (book, summary) => {
    const id = bookId(book);
    asinResearchDraftFor(book, summary);
    const output = keywordOutputFor(id, summary);
    const keywords = String(valueFor(output, "adsKeyword", "") ?? "").split(",").map((item) => item.trim()).filter(Boolean);
    const session = asinSessionFor(id);
    const view = asinSessionView(session);
    const rows = valueFor(view, "rows", []);
    const outcome = asinOutcomeName(valueFor(view, "outcome", "Idle"));
    const active = asinSessionActive(session);
    const crawlBusy = active || Boolean(state.asinResearchActiveBookId);
    const cancelling = valueFor(session, "isCancelling", false);
    const finalAsins = asinFinalValue(session);
    const browserState = browserStateName(valueFor(state.amazonBrowserStatus, "state", "Closed"));
    const browserBusy = state.amazonBrowserPending || ["Checking", "Downloading", "Opening", "WarmingUp"].includes(browserState);
    const marketName = String(valueFor(state.amazonBrowserStatus, "targetMarketName", valueFor(view, "marketName", "")) ?? "");
    const marketDomain = String(valueFor(state.amazonBrowserStatus, "targetDomain", valueFor(view, "marketplaceDomain", "")) ?? "");
    const needsAttention = browserState === "NeedsAttention" || outcome === "NeedsAttention";
    const previewState = keywordPreviewFor(id);
    const hasTrustedSource = Boolean(valueFor(previewState, "receipt", ""));
    const validInput = hasTrustedSource && keywords.length > 0 && keywords.length <= 30;
    const stale = asinResultIsStale(id);
    const feedback = asinFeedbackFor(id);
    const completed = Number(valueFor(view, "completedCount", 0)) || 0;
    const total = Number(valueFor(view, "totalCount", keywords.length)) || keywords.length;
    const selectedCount = rows.filter((row) => asinRowStatusName(valueFor(row, "status", "Pending")) === "Selected").length;
    const failedCount = rows.filter((row) => ["Failed", "Cancelled"].includes(asinRowStatusName(valueFor(row, "status", "Pending")))).length;
    const noMatchCount = rows.filter((row) => ["NoSearchResult", "NoMatchingTitle", "AllCandidatesUsed"].includes(asinRowStatusName(valueFor(row, "status", "Pending")))).length;
    const rowMarkup = rows.map((row) => {
      const rowStatus = asinRowStatusName(valueFor(row, "status", "Pending"));
      const asin = String(valueFor(row, "asin", "") ?? "");
      const label = rowStatus === "Selected" ? `Selected — ${asin}` : rowStatus === "Searching" ? "Searching…" : rowStatus === "Pending" ? "Waiting" : asinReasonLabel(valueFor(row, "reasonCode", ""));
      return `<li class="asin-result-row asin-result-${rowStatus.toLowerCase()}"><span>${escapeHtml(valueFor(row, "keyword", ""))}</span><strong>${escapeHtml(label)}</strong></li>`;
    }).join("");
    const stateCopy = browserBusy
      ? browserState === "WarmingUp" ? "Waiting for Amazon session…" : "Downloading or opening browser components…"
      : needsAttention ? "Open the browser and resolve Amazon's prompt, then crawl again."
        : outcome === "Completed" ? "Crawl completed."
          : outcome === "Partial" ? "Crawl completed with partial results."
            : outcome === "Cancelled" ? "Crawl cancelled; available results were kept."
              : outcome === "Failed" ? "Crawl stopped. Review the result below."
                : "Open Browser is optional; Crawl ASINs opens it automatically.";
    return `<fieldset class="keyword-builder-group keyword-builder-crawl" data-asin-research data-book-id="${escapeHtml(id)}" aria-labelledby="asin-research-results-title" aria-busy="${crawlBusy || browserBusy}">
      <legend id="asin-research-results-title">Crawl Results</legend>
      <div class="asin-research-pane-heading"><p class="asin-result-summary"><strong>${selectedCount} selected</strong><span>${noMatchCount} no match · ${failedCount} failed</span></p><span data-asin-status class="status-badge ${needsAttention || outcome === "Failed" ? "status-bad" : active || browserBusy ? "status-warn" : browserState === "Ready" ? "status-good" : "status-muted"}">${escapeHtml(active ? cancelling ? "Cancelling" : "Running" : outcome !== "Idle" ? outcome : browserState)}</span></div>
      <p class="asin-keyword-source">Uses generated Ads Keyword · <span data-asin-keyword-count>${keywords.length} / 30</span></p>
      <p class="asin-keyword-source">Amazon Market: ${escapeHtml(marketName || "Resolving…")}${marketDomain ? ` — ${escapeHtml(marketDomain)}` : ""}</p>
      <div class="asin-progress-slot">${active && total ? `<div class="asin-progress" role="progressbar" aria-label="Amazon ASIN crawl progress" aria-valuemin="0" aria-valuemax="${total}" aria-valuenow="${completed}"><span style="width:${Math.round(completed / total * 100)}%"></span></div>` : ""}</div>
      <ol class="asin-result-list">${rowMarkup || '<li class="asin-result-empty">No crawl results yet.</li>'}</ol>
      <div class="asin-stale-slot">${stale && finalAsins ? '<p class="catalog-warning" role="status">Previous results — shuffle again before applying.</p>' : ""}</div>
      <footer class="asin-research-pane-footer"><div class="asin-research-actions"><button class="button-secondary" data-action="open-amazon-browser" data-book-id="${escapeHtml(id)}" ${crawlBusy || browserBusy ? "disabled" : ""}>${browserBusy ? "Opening Browser…" : "Open Browser"}</button><button class="button-secondary" data-action="crawl-amazon-asins" data-book-id="${escapeHtml(id)}" title="${validInput ? "Search with the generated Ads Keyword preview" : "Shuffle inputs before crawling"}" ${!validInput || crawlBusy || browserBusy ? "disabled" : ""}>Crawl ASINs</button>${active ? `<button class="button-secondary" data-action="cancel-amazon-asins" data-book-id="${escapeHtml(id)}" ${cancelling ? "disabled" : ""}>${cancelling ? "Cancelling…" : "Cancel"}</button>` : ""}</div><p class="asin-research-state" role="${needsAttention ? "alert" : "status"}">${escapeHtml(stateCopy)}</p></footer>
      <p class="catalog-feedback ${feedback.error ? "is-error" : ""}" data-asin-feedback role="${feedback.error ? "alert" : "status"}" aria-live="polite" aria-atomic="true">${escapeHtml(feedback.message)}</p>
    </fieldset>`;
  };

  const renderBookKeywordBuilder = (book, summary) => {
    const id = bookId(book);
    const saved = keywordBuilderFor(summary);
    const previewState = keywordPreviewFor(id);
    const output = keywordOutputFor(id, summary);
    const draft = keywordBuilderDraftFor(book, summary);
    const normalizedDraft = normalizeKeywordBuilderDraft(draft);
    const dirty = hasKeywordBuilderDraft(book, summary);
    const validation = state.bookKeywordBuilderValidation.get(id);
    const pendingAction = keywordPendingFor(id);
    const pending = Boolean(pendingAction) || state.catalogMutationPending && state.catalogMutationCommand === "book.keywords.save" && state.catalogMutationTarget === id;
    const refreshing = state.keywordBuilderRefreshPending && state.keywordBuilderRefreshBookId === id;
    const refreshNeeded = state.keywordBuilderRefreshNeeded && state.keywordBuilderRefreshBookId === id;
    const feedbackVisible = state.catalogMutationTarget === id && state.catalogMutationCommand.startsWith("book.keywords.");
    const genericKeywordCount = persistedGenericKeywords(languageCodeFor(summary)).length;
    const disabled = catalogMutationBusy() || processIsActive();
    const stateLabel = pendingAction === "shuffle" ? "Shuffling…" : pendingAction === "save" ? "Saving…" : pendingAction === "update-ads-asin" ? "Applying ASINs…" : validation ? "Needs attention" : refreshing ? "Saved · Refreshing…" : refreshNeeded ? "Saved · Refresh needed" : previewState ? "Preview · Not saved" : dirty ? "Shuffle required" : saved ? "Saved" : "Not shuffled";
    const outputMessage = previewState ? "Unsaved shuffled preview" : saved ? "Last saved generated keywords" : "Shuffle inputs to generate a preview.";
    const keywordRows = Array.from({ length: 7 }, (_, index) => {
      const name = `keyword_${index + 1}`;
      const value = String(valueFor(output, name, "") ?? "");
      const count = metadataGraphemeCount(value);
      const counter = count === null ? "≤ 50" : `${count} / 50`;
      return `<label class="keyword-builder-output-field" for="book-${name}-output"><span>Keyword ${index + 1}</span><input id="book-${name}-output" class="control" readonly value="${escapeHtml(value)}" aria-describedby="book-${name}-count"><small id="book-${name}-count">${counter}</small></label>`;
    }).join("");
    const errorMessage = validation?.message ?? "";
    return `<section class="keyword-builder-workspace" data-book-keyword-builder-card aria-label="Keyword workspace" aria-busy="${pending}">
      <div class="keyword-builder-grid">
        <div class="keyword-builder-column">
          <fieldset class="keyword-builder-group keyword-builder-inputs" aria-labelledby="keyword-builder-inputs-title">
            <legend id="keyword-builder-inputs-title">Build inputs</legend>
            <div class="keyword-builder-group-summary">
              <p>Generic Keywords are applied first; Book Keywords fill the remaining space when shuffled.</p>
              <span class="keyword-builder-source-badge">${genericKeywordCount} Generic</span>
            </div>
            <label class="field keyword-builder-source-field" for="book-keyword-source">
              <span>Book Keywords</span>
              <textarea id="book-keyword-source" class="control keyword-list-input ${validation ? "control-invalid" : ""}" rows="5" data-action="book-keyword-source" data-book-id="${escapeHtml(id)}" aria-describedby="book-keyword-source-help book-keyword-source-error" aria-invalid="${Boolean(validation)}" autocomplete="off" spellcheck="false" ${disabled ? "disabled" : ""}>${escapeHtml(draft.sourceText)}</textarea>
              <small id="book-keyword-source-help">One phrase per line. Phrases matching Generic Keywords are excluded during Shuffle.</small>
              <small id="book-keyword-source-error" class="field-error" ${errorMessage ? "" : "hidden"}>${escapeHtml(errorMessage)}</small>
            </label>
          </fieldset>
          ${renderAsinResearch(book, summary)}
        </div>
        <fieldset class="keyword-builder-group keyword-builder-outputs" aria-labelledby="keyword-builder-output-title">
          <legend id="keyword-builder-output-title">Generated output</legend>
          <div class="keyword-builder-output-heading">
            <p>${escapeHtml(outputMessage)}${output ? ` · ${escapeHtml(dateTime(valueFor(output, "builtAtUtc", null)))}` : ""}</p>
            ${badge(stateLabel)}
          </div>
          <div class="keyword-builder-output-list">${keywordRows}</div>
          <label class="field" for="book-ads-keyword-output">
            <span>Ads Keyword</span>
            <textarea id="book-ads-keyword-output" class="control keyword-ads-output" rows="4" readonly aria-readonly="true">${escapeHtml(valueFor(output, "adsKeyword", "") ?? "")}</textarea>
          </label>
          <label class="field" for="book-keyword-ads-asin">
            <span>Ads ASIN (product targets)</span>
            <input id="book-keyword-ads-asin" class="control" type="text" data-action="book-keyword-ads-asin" data-book-id="${escapeHtml(id)}" value="${escapeHtml(draft.adsAsin)}" aria-describedby="book-keyword-ads-asin-help" autocomplete="off" spellcheck="false" ${disabled ? "disabled" : ""}>
            <small id="book-keyword-ads-asin-help">Optional comma-separated product targets. Empty values are ignored during Build.</small>
          </label>
          <div class="keyword-builder-actions">
            <button class="button-secondary keyword-builder-build" data-action="shuffle-book-keywords" data-book-id="${escapeHtml(id)}" ${disabled || pending ? "disabled" : ""}>${pendingAction === "shuffle" ? "Shuffling…" : "Shuffle"}</button>
            <button class="button-primary" data-action="save-book-keywords" data-book-id="${escapeHtml(id)}" title="${previewState ? "Save this exact preview" : "Shuffle inputs before saving"}" ${disabled || pending || !valueFor(previewState, "receipt", "") ? "disabled" : ""}>${pendingAction === "save" ? "Saving…" : "Save"}</button>
            <button class="button-secondary" data-action="copy-book-keywords" data-book-id="${escapeHtml(id)}" ${output ? "" : "disabled"}>Copy to Clipboard</button>
          </div>
          <div class="catalog-actions keyword-builder-status"><p class="catalog-feedback ${feedbackVisible && state.catalogFeedbackError ? "is-error" : ""}" data-catalog-feedback="keywords" role="${feedbackVisible && state.catalogFeedbackError ? "alert" : "status"}" aria-live="polite" aria-atomic="true">${feedbackVisible ? escapeHtml(state.catalogFeedback) : ""}</p>${refreshNeeded ? `<button class="button-secondary" data-action="retry-keyword-refresh" data-book-id="${escapeHtml(id)}" ${applicationIsLoading() ? "disabled" : ""}>Retry refresh</button>` : ""}</div>
        </fieldset>
      </div>
    </section>`;
  };

  const renderAsinResearchWorkspace = (book, summary) => `<div class="asin-research-workspace">
    ${renderBookKeywordBuilder(book, summary)}
  </div>`;

  const renderBrandTemplateCopyCard = (book, summary) => {
    const readiness = brandTemplateCopyReadiness(book, summary);
    const assigned = assignedBrandName(summary) || "Unassigned";
    return `<fieldset class="asset-background-setting book-settings-card book-settings-templates" data-brand-template-copy-card><legend>Brand PSD templates</legend><div class="book-settings-inline"><div><p>Assigned Brand: <strong>${escapeHtml(assigned)}</strong>. Copy cover.psd, app_plus.psd, and book_owner.psd into this Book workspace.</p><small>${escapeHtml(readiness.reason)}</small></div><button class="button-secondary" data-action="copy-brand-templates" data-book-id="${escapeHtml(bookId(book))}" ${readiness.ready && !state.brandTemplateCopyPending ? "" : "disabled"} title="${escapeHtml(readiness.reason)}">${state.brandTemplateCopyPending ? "Copying…" : "Copy Brand Templates"}</button></div></fieldset>`;
  };

  const refreshProductionWorkspace = (focusSelector = "") => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const workspace = document.querySelector(".production-workspace");
    if (!book || !summary || !workspace) return;
    const drawerBody = document.querySelector(".book-drawer-body");
    if (drawerBody && Number.isFinite(drawerBody.scrollTop)) state.bookDrawerScrollTop = drawerBody.scrollTop;
    workspace.outerHTML = renderProductionWorkspace(book, summary);
    const refreshedDrawerBody = document.querySelector(".book-drawer-body");
    if (refreshedDrawerBody && Number.isFinite(state.bookDrawerScrollTop)) refreshedDrawerBody.scrollTop = state.bookDrawerScrollTop;
    if (focusSelector) document.querySelector(focusSelector)?.focus();
  };

  const renderFrameModeMigrationWarning = (summary) => {
    const migratedCount = Number(valueFor(summary, "legacyFrameModePageCount", 0)) || 0;
    return migratedCount > 0
      ? `<section class="catalog-warning artwork-migration-warning" role="status"><strong>Frame mode updated</strong><p>${migratedCount} Interior page${migratedCount === 1 ? "" : "s"} previously using Auto now use No Frame. Review Interior artwork before reprocessing.</p><button type="button" class="button-secondary" data-action="book-tab" data-book-tab="artwork">Review Interior artwork</button></section>`
      : "";
  };

  const renderBookTabs = (book, summary) => {
    if (!workspaceStateAvailable(summary)) {
      return `<div class="book-heading"><div><h2>${escapeHtml(bookDisplayTitle(book, summary))}</h2><p>Folder: ${escapeHtml(valueFor(book, "name", bookId(book)))}</p></div></div><section class="process-failure" role="alert"><strong>Workspace state unavailable</strong><p>${escapeHtml(valueFor(summary, "workspaceStateError", "Restore or repair the workspace state file, then refresh the library."))}</p><p>No Book settings or processing actions are available, and the original state file has not been changed.</p></section>`;
    }
    const tabButton = (id, label) => `<button type="button" id="book-tab-${id}" class="detail-tab ${state.selectedBookTab === id ? "active" : ""}" data-action="book-tab" data-book-tab="${id}" role="tab" aria-selected="${state.selectedBookTab === id}" aria-controls="book-panel-${id}" tabindex="${state.selectedBookTab === id ? "0" : "-1"}">${label}</button>`;
    const readiness = processingReadiness(book, summary);
    const backgroundSetting = `<fieldset class="asset-background-setting book-settings-card book-settings-background"><legend>Brand background</legend><div class="book-settings-inline"><p>Insert the assigned Brand background after every active Interior page.</p><label class="asset-background-toggle"><input type="checkbox" data-action="set-book-background" data-book-id="${escapeHtml(bookId(book))}" ${effectiveBackground(book, summary) ? "checked" : ""} ${processIsActive() || state.bookInteriorSavePending ? "disabled" : ""}> Use Brand background</label></div></fieldset>`;
    const body = state.selectedBookTab === "asin"
      ? renderAsinResearchWorkspace(book, summary)
      : state.selectedBookTab === "production"
      ? renderProductionWorkspace(book, summary)
      : state.selectedBookTab === "settings"
      ? `<section class="book-settings-workspace" aria-label="Book settings">${renderBookInformation(book, summary)}${renderBookCompletion(book, summary)}${renderBookBrandAssignment(book, summary)}${backgroundSetting}${renderBrandTemplateCopyCard(book, summary)}${renderBookS3Storage(book)}</section>`
      : state.selectedBookTab === "artwork"
        ? renderFolderAssetWorkspace(book, summary)
        : state.selectedBookTab === "pages"
          ? renderProcessedInteriorPages(summary)
        : `<section class="book-settings-workspace" aria-label="Book settings">${renderBookInformation(book, summary)}${renderBookCompletion(book, summary)}${renderBookBrandAssignment(book, summary)}${backgroundSetting}${renderBrandTemplateCopyCard(book, summary)}${renderBookS3Storage(book)}</section>`;
    return `<div class="book-heading"><div><h2>${escapeHtml(bookDisplayTitle(book, summary))}</h2><p>Folder: ${escapeHtml(valueFor(book, "name", bookId(book)))}</p></div><div class="page-actions"><button class="button-secondary" data-action="validate-book" data-book-id="${escapeHtml(bookId(book))}">Run Interior preflight</button><button class="button-primary" data-action="queue-selected-book" ${readiness.ready ? "" : "disabled"} title="${escapeHtml(readiness.reason)}" aria-label="Process Interior. ${escapeHtml(readiness.reason)}">Process Interior</button></div></div><nav class="detail-tabs" role="tablist" aria-label="Book detail sections">${tabButton("settings", "Settings")}${tabButton("asin", "Keyword")}${tabButton("production", "Production")}${tabButton("artwork", "Interior artwork")}</nav><div id="book-panel-${state.selectedBookTab}" class="tab-body ${state.selectedBookTab === "settings" ? "tab-body-settings" : state.selectedBookTab === "asin" ? "tab-body-asin" : state.selectedBookTab === "artwork" ? "tab-body-artwork" : state.selectedBookTab === "pages" ? "tab-body-processed-pages" : ""}" role="tabpanel" aria-labelledby="book-tab-${state.selectedBookTab}" tabindex="0">${body}</div>`;
  };

  const renderIntroTemplateWorkspace = (book, summary) => {
    const assignment = assignmentReadiness(summary);
    const brand = assignment.ready ? assignment.brand : null;
    const allTemplates = valueFor(brand, "introTemplateAssets", []) ?? [];
    const templates = allTemplates.filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", "")));
    const selection = effectiveIntro(book, summary);
    const candidates = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior");
    const byReference = new Map(candidates.map((asset) => [String(valueFor(asset, "sourceReference", "")).toLowerCase(), asset]));
    const selected = selection.sourceReferences.map((reference) => byReference.get(String(reference).toLowerCase())).filter(Boolean);
    const disabled = processIsActive() || state.bookInteriorSavePending;
    const readiness = introReadiness(book, summary);
    const selectedTile = (asset, index) => {
      const reference = valueFor(asset, "sourceReference", "");
      return `<article class="intro-template-tile"><span class="intro-template-preview">${localImageMarkup(asset, `Custom Intro ${valueFor(asset, "fileName", "")}`, "Image unavailable")}</span><strong>Intro #${index + 1}</strong><span title="${escapeHtml(valueFor(asset, "fileName", ""))}">${escapeHtml(valueFor(asset, "fileName", ""))}</span><div class="intro-template-actions"><button class="button-secondary" data-action="intro-move-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-index="${index}" data-intro-direction="up" aria-label="Move ${escapeHtml(valueFor(asset, "fileName", ""))} earlier" ${index === 0 || disabled ? "disabled" : ""}>Earlier</button><button class="button-secondary" data-action="intro-move-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-index="${index}" data-intro-direction="down" aria-label="Move ${escapeHtml(valueFor(asset, "fileName", ""))} later" ${index === selected.length - 1 || disabled ? "disabled" : ""}>Later</button><button class="button-secondary" data-action="intro-remove-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-source-reference="${escapeHtml(reference)}" ${disabled ? "disabled" : ""}>Remove</button></div></article>`;
    };
    const availableOption = (asset) => `<button class="intro-template-add" data-action="intro-add-template" data-book-id="${escapeHtml(bookId(book))}" data-intro-source-reference="${escapeHtml(valueFor(asset, "sourceReference", ""))}" ${disabled ? "disabled" : ""}>${localImageMarkup(asset, `Available Book interior page ${valueFor(asset, "fileName", "")}`, "Image unavailable")}<span>${escapeHtml(valueFor(asset, "fileName", ""))}</span><small>Add as Intro</small></button>`;
    const brandCopy = brand ? `${escapeHtml(valueFor(brand, "name", ""))} · ${templates.length} eligible local template${templates.length === 1 ? "" : "s"}` : escapeHtml(assignment.reason);
    const allItems = selection.hasIntro ? candidates : templates;
    const introPageSize = 6;
    const totalPages = Math.max(1, Math.ceil(allItems.length / introPageSize));
    state.introTemplatePage = Math.min(totalPages, Math.max(1, state.introTemplatePage));
    const start = (state.introTemplatePage - 1) * introPageSize;
    const pageItems = allItems.slice(start, start + introPageSize);
    const visibleItems = selection.hasIntro
      ? pageItems.map((asset) => {
        const index = selection.sourceReferences.findIndex((reference) => String(reference).toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase());
        return index >= 0 ? selectedTile(asset, index) : availableOption(asset);
      }).join("")
      : pageItems.map((asset) => `<article class="intro-template-tile"><span class="intro-template-preview">${introTemplateImageMarkup(asset, `Automatic Intro template ${valueFor(asset, "fileName", "")}`, valueFor(brand, "name", ""))}</span><strong>Automatic</strong><span title="${escapeHtml(valueFor(asset, "fileName", ""))}">${escapeHtml(valueFor(asset, "fileName", ""))}</span></article>`).join("");
    const paging = `<footer class="intro-template-pagination" data-intro-total-pages="${totalPages}"><span>${allItems.length ? `${start + 1}–${Math.min(start + introPageSize, allItems.length)} of ${allItems.length}` : "0 pages"}</span><div><button class="button-secondary" data-action="intro-template-page" data-intro-template-page="previous" ${state.introTemplatePage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.introTemplatePage} of ${totalPages}</span><button class="button-secondary" data-action="intro-template-page" data-intro-template-page="next" ${state.introTemplatePage === totalPages ? "disabled" : ""}>Next</button></div></footer>`;
    const sourceCopy = selection.hasIntro
      ? `${selected.length ? `${selected.length} selected Intro page${selected.length === 1 ? "" : "s"}. Use each card to preserve order or remove it.` : "Select at least one Book interior page to make this Book ready."}`
      : `All eligible templates are added in filename order. ${finalInteriorPageSize().width} × ${finalInteriorPageSize().height} Brand artwork is assembled directly into the PDF; 1024 × 1024 and 2048 × 2048 templates retain the legacy processing path. Book interior pages remain eligible for normal Interior processing.`;
    return `<section class="intro-template-workspace"><div class="intro-template-heading"><div><h3>Intro pages</h3><p>${selection.hasIntro ? "Choose ordered pages from this Book's Book interior." : brandCopy}</p></div><span class="status-badge ${selection.hasIntro ? "status-warn" : "status-muted"}">${selection.hasIntro ? "Custom Book interior" : "Automatic Brand template"}</span></div><p class="${readiness.ready ? "panel-note" : "intro-template-warning"}" role="${readiness.ready ? "status" : "alert"}">${readiness.ready ? "Ready for backend size validation during processing." : escapeHtml(readiness.reason)}</p><fieldset class="intro-mode-choice" ${disabled ? "disabled" : ""}><legend>Intro source</legend><label><input type="radio" name="intro-mode" data-action="set-intro-mode" data-book-id="${escapeHtml(bookId(book))}" value="auto" ${selection.hasIntro ? "" : "checked"}> Automatic <small>Use every eligible current Brand IntroTemplate in filename order.</small></label><label><input type="radio" name="intro-mode" data-action="set-intro-mode" data-book-id="${escapeHtml(bookId(book))}" value="custom" ${selection.hasIntro ? "checked" : ""}> Custom <small>Choose Book interior pages and their print order.</small></label></fieldset><div class="intro-template-selection"><div><h4>${selection.hasIntro ? "Book interior pages" : "Automatic Brand IntroTemplate"}</h4><p>${sourceCopy}</p></div><div class="intro-template-page-grid">${visibleItems || "<p class=\"empty-copy\">No eligible Intro pages are available.</p>"}</div>${paging}</div></section>`;
  };

  const refreshIntroTemplateWorkspace = (focusPageAction = "") => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const workspace = document.querySelector(".intro-template-workspace");
    if (!book || !summary || !workspace) { render("books", false); return; }
    workspace.outerHTML = renderIntroTemplateWorkspace(book, summary);
    updateInteriorSaveUi();
    const oppositeAction = focusPageAction === "next" ? "previous" : "next";
    document.querySelector(`[data-action="intro-template-page"][data-intro-template-page="${oppositeAction}"]`)?.focus();
  };

  const renderFolderAssetWorkspace = (book, summary) => {
    const intro = effectiveIntro(book, summary);
    const isCustomIntro = (asset) => intro.hasIntro && intro.sourceReferences.some((item) => item.toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase());
    const allAssets = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior" && !isCustomIntro(asset));
    const sourceFolderNames = valueFor(summary, "sourceFolders", []).map((folder) => valueFor(folder, "name", "")).filter(Boolean);
    const folderFor = (asset) => sourceFolderNames.find((name) => valueFor(asset, "relativePath", "").replaceAll("\\", "/").toLowerCase().startsWith(`${name.toLowerCase()}/`)) ?? valueFor(asset, "folder", "Other");
    const matchesStatus = (asset) => !state.assetStatus || (state.assetStatus === "Active" ? effectiveInteriorAsset(book, asset).isActive : !effectiveInteriorAsset(book, asset).isActive);
    const matchesFrameMode = (asset) => !state.assetFrameMode || effectiveInteriorAsset(book, asset).frameMode === state.assetFrameMode;
    const matching = allAssets.filter((asset) => matchesStatus(asset) && matchesFrameMode(asset));
    const eligibleMatching = matching;
    const eligibleReferences = new Set(allAssets.map((asset) => String(valueFor(asset, "sourceReference", ""))));
    state.selectedArtworkReferences = new Set([...state.selectedArtworkReferences].filter((reference) => eligibleReferences.has(reference)));
    const selectedVisibleCount = eligibleMatching.filter((asset) => state.selectedArtworkReferences.has(String(valueFor(asset, "sourceReference", "")))).length;
    const selectedCount = state.selectedArtworkReferences.size;
    const tile = (asset) => {
      const settings = effectiveInteriorAsset(book, asset);
      const mode = settings.frameMode;
      const reference = valueFor(asset, "sourceReference", "");
      const active = settings.isActive;
      const selected = state.selectedArtworkReferences.has(String(reference));
      const disabled = processIsActive() || state.bookInteriorSavePending ? "disabled" : "";
      const statusBadge = active ? `<span class="status-badge status-good">Active</span>` : `<span class="status-badge status-bad">Inactive</span>`;
      const frameBadge = `<span class="artwork-frame-badge artwork-frame-${escapeHtml(mode)}">${mode === "enabled" ? "Frame" : "No Frame"}</span>`;
      const cardClass = `interior-artwork-card ${active ? "is-active" : "is-inactive"} ${selected ? "is-selected" : ""}`;
      const cardContent = `<div class="interior-artwork-preview"><span class="artwork-card-selection-indicator">${selected ? "Selected" : "Select"}</span>${localImageMarkup(asset, `Preview of ${valueFor(asset, "fileName", "asset")}`, "Image unavailable")}</div><div class="interior-artwork-copy"><strong title="${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}">${escapeHtml(valueFor(asset, "fileName", "Unnamed asset"))}</strong><small title="${escapeHtml(folderFor(asset))}">${escapeHtml(folderFor(asset))} · ${escapeHtml(assetDimensions(asset))}</small><div class="interior-artwork-badges">${statusBadge}${frameBadge}</div></div>`;
      return `<button type="button" class="${cardClass}" data-action="toggle-artwork-selection" data-source-reference="${escapeHtml(reference)}" aria-pressed="${selected}" aria-label="${selected ? "Deselect" : "Select"} ${escapeHtml(valueFor(asset, "fileName", "artwork"))}; ${active ? "Active" : "Inactive"}; ${mode === "enabled" ? "Frame" : "No Frame"}" ${disabled}>${cardContent}</button>`;
    };
    const activeCount = allAssets.filter((asset) => effectiveInteriorAsset(book, asset).isActive).length;
    const inactiveCount = allAssets.length - activeCount;
    const controlsDisabled = processIsActive() || state.bookInteriorSavePending;
    const shuffle = valueFor(summary, "interiorShuffle", {});
    const shuffleState = String(valueFor(shuffle, "status", "Missing"));
    const interiorDirty = hasInteriorDraft(bookId(book));
    const canRandomize = Boolean(valueFor(shuffle, "canRandomize", false));
    const shuffleDisabled = controlsDisabled || state.interiorShufflePending || interiorDirty || !canRandomize;
    const shuffleTitle = interiorDirty
      ? "Save Interior changes first"
      : processIsActive() ? "Wait for Interior processing to finish"
        : state.bookInteriorSavePending ? "Wait for Interior changes to finish saving"
          : !canRandomize ? "Activate at least one Interior page before randomizing"
            : "Create and save a new random order for the active Interior pages";
    const defaultShuffleFeedback = shuffleState === "Current"
      ? ""
      : shuffleState === "Stale"
        ? "Random order is out of date. Random again before processing."
        : "Random order is required before processing.";
    const shuffleFeedback = state.interiorShuffleFeedback || defaultShuffleFeedback;
    const shuffleStatus = shuffleFeedback ? `<small id="interior-shuffle-status" class="interior-shuffle-status ${state.interiorShuffleFeedbackError ? "is-error" : ""}" role="${state.interiorShuffleFeedbackError ? "alert" : "status"}" aria-live="polite">${escapeHtml(shuffleFeedback)}</small>` : "";
    const shuffleDescription = shuffleFeedback ? 'aria-describedby="interior-shuffle-status"' : "";
    const allShownSelected = eligibleMatching.length > 0 && selectedVisibleCount === eligibleMatching.length;
    const bulkDisabled = !selectedCount || (state.assetBulkActive === "unchanged" && state.assetBulkFrameMode === "unchanged") || controlsDisabled;
    const filterControls = `<div class="asset-filter-controls"><span class="asset-filter-label">Filter artwork</span><div class="asset-filter-row"><div class="asset-filter-chip-groups"><div class="asset-status-filter" role="group" aria-label="Interior artwork status filters">${["Active", "Inactive"].map((name) => `<button type="button" class="${state.assetStatus === name ? "active" : ""}" data-action="asset-status" data-asset-status="${name}" aria-pressed="${state.assetStatus === name}">${name}</button>`).join("")}</div><div class="asset-frame-filter" role="group" aria-label="Interior artwork frame mode filters">${[["", "All"], ["enabled", "Frame"], ["disabled", "No Frame"]].map(([value, label]) => `<button type="button" class="${state.assetFrameMode === value ? "active" : ""}" data-action="asset-frame-mode" data-asset-frame-mode="${value}" aria-pressed="${state.assetFrameMode === value}">${label}</button>`).join("")}</div></div></div>${shuffleStatus}</div>`;
    const folderOpenPending = state.interiorFolderOpenPending && state.interiorFolderOpenBookId === bookId(book);
    const bulkControls = `<div class="interior-artwork-bulk-controls"><label class="artwork-select-all"><input type="checkbox" data-action="toggle-all-artwork" ${allShownSelected ? "checked" : ""} ${!eligibleMatching.length || controlsDisabled ? "disabled" : ""}> Select all ${eligibleMatching.length} shown</label><label class="field artwork-bulk-field"><span>Status</span><select class="control h-8" data-action="set-artwork-bulk-active"><option value="unchanged" ${state.assetBulkActive === "unchanged" ? "selected" : ""}>No change</option><option value="active" ${state.assetBulkActive === "active" ? "selected" : ""}>Active</option><option value="inactive" ${state.assetBulkActive === "inactive" ? "selected" : ""}>Inactive</option></select></label><label class="field artwork-bulk-field"><span>Frame mode</span><select class="control h-8" data-action="set-artwork-bulk-frame-mode"><option value="unchanged" ${state.assetBulkFrameMode === "unchanged" ? "selected" : ""}>No change</option><option value="enabled" ${state.assetBulkFrameMode === "enabled" ? "selected" : ""}>Frame</option><option value="disabled" ${state.assetBulkFrameMode === "disabled" ? "selected" : ""}>No Frame</option></select></label><div class="interior-artwork-actions"><button type="button" class="button-secondary interior-folder-button" data-action="open-interior-folder" data-book-id="${escapeHtml(bookId(book))}" title="Open the Book interior source folder" aria-busy="${folderOpenPending}" ${state.interiorFolderOpenPending ? "disabled" : ""}>${folderOpenPending ? "Opening…" : "Open Folder"}</button><button type="button" class="button-secondary interior-shuffle-button" data-action="random-interior" data-book-id="${escapeHtml(bookId(book))}" title="${escapeHtml(shuffleTitle)}" ${shuffleDescription} aria-busy="${state.interiorShufflePending}" ${shuffleDisabled ? "disabled" : ""}>${state.interiorShufflePending ? "Randomizing…" : "Random Interior"}</button><button class="button-primary" data-action="apply-artwork-bulk" ${bulkDisabled ? "disabled" : ""}>Apply to ${selectedCount} selected</button></div></div>`;
    return `<section class="interior-artwork-workspace">${renderFrameModeMigrationWarning(summary)}<header class="interior-artwork-heading"><div><h3>Interior artwork</h3><p>Review every available Book interior page, then control whether it is processed and which frame mode it uses.</p></div><p class="interior-artwork-count" role="status" aria-live="polite" aria-atomic="true"><strong>${matching.length}</strong> shown · <strong>${allAssets.length}</strong> total · <strong>${activeCount}</strong> active · <strong>${inactiveCount}</strong> inactive</p></header><fieldset class="interior-artwork-toolbar"><legend>Artwork controls</legend>${filterControls}${bulkControls}</fieldset><div class="interior-artwork-grid-scroll"><div class="interior-artwork-grid">${matching.length ? matching.map(tile).join("") : `<p class="empty-copy interior-artwork-empty">No artwork matches this view. <button type="button" class="button-link" data-action="clear-artwork-filters">Clear filters</button></p>`}</div></div></section>`;
  };

  const refreshInteriorArtworkWorkspace = () => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const workspace = document.querySelector(".interior-artwork-workspace");
    if (!book || !summary || !workspace) { render("books", false); return; }
    const grid = workspace.querySelector?.(".interior-artwork-grid-scroll") ?? document.querySelector(".interior-artwork-grid-scroll");
    if (grid && Number.isFinite(grid.scrollTop)) state.artworkGridScrollTop = grid.scrollTop;
    workspace.outerHTML = renderFolderAssetWorkspace(book, summary);
    const refreshedGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (refreshedGrid) refreshedGrid.scrollTop = state.artworkGridScrollTop;
    updateInteriorSaveUi();
  };

  const refreshBookDrawerBody = (focusTab = "") => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const drawerBody = document.querySelector(".book-drawer-body");
    if (!book || !summary || !drawerBody) { render("books", false); return; }
    if (Number.isFinite(drawerBody.scrollTop)) state.bookDrawerScrollTop = drawerBody.scrollTop;
    const artworkGrid = drawerBody.querySelector?.(".interior-artwork-grid-scroll") ?? document.querySelector(".interior-artwork-grid-scroll");
    if (artworkGrid && Number.isFinite(artworkGrid.scrollTop)) state.artworkGridScrollTop = artworkGrid.scrollTop;
    drawerBody.innerHTML = renderBookTabs(book, summary);
    if (Number.isFinite(state.bookDrawerScrollTop)) drawerBody.scrollTop = state.bookDrawerScrollTop;
    const refreshedGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (refreshedGrid && Number.isFinite(state.artworkGridScrollTop)) refreshedGrid.scrollTop = state.artworkGridScrollTop;
    updateInteriorSaveUi();
    if (focusTab) document.querySelector(`[data-action="book-tab"][data-book-tab="${focusTab}"]`)?.focus();
  };

  const renderBookCloneGroup = (book) => {
    const id = bookId(book);
    const sourceName = String(valueFor(book, "name", id));
    const destination = bookCloneDestinationName(sourceName, state.bookCloneLanguageCode);
    const destinationExists = Boolean(destination) && books().some((candidate) => String(valueFor(candidate, "name", bookId(candidate))).toLocaleLowerCase() === destination.toLocaleLowerCase());
    const busy = state.bookClonePending || state.bookCloneAwaitingSnapshot;
    const writerActive = processIsActive() || productionActionActive() || state.cacheCleanupActive || applicationIsLoading();
    const blocked = !state.bookCloneLanguageCode || !destination || destinationExists || busy || writerActive;
    const feedback = destinationExists && !state.bookCloneFeedback
      ? "That destination Book already exists. Select another language."
      : state.bookCloneFeedback;
    const feedbackError = destinationExists || state.bookCloneFeedbackError;
    const notice = state.bookCloneNotice && state.bookCloneNoticeBookId === id ? `<p class="brand-clone-notice book-clone-notice" role="status" aria-live="polite">${escapeHtml(state.bookCloneNotice)}</p>` : "";
    if (!state.bookCloneOpen || state.bookCloneSourceId !== id) return notice ? `<div class="book-clone-region">${notice}</div>` : "";
    const languageOptions = supportedLanguages().map((option) => {
      const code = String(valueFor(option, "code", ""));
      return `<option value="${escapeHtml(code)}" ${code === state.bookCloneLanguageCode ? "selected" : ""}>${escapeHtml(valueFor(option, "name", code))}</option>`;
    }).join("");
    return `<div class="book-clone-region">${notice}<fieldset class="brand-clone-group book-clone-group" aria-busy="${busy}" ${busy ? "disabled" : ""}><legend>Clone Book</legend><div class="brand-clone-grid"><label class="field"><span>Source Book</span><input class="control" value="${escapeHtml(sourceName)}" readonly aria-readonly="true"></label><label class="field"><span>Language</span><select class="control" data-action="clone-book-language" aria-describedby="book-clone-feedback"><option value="">Select language</option>${languageOptions}</select></label><label class="field"><span>Destination Book name</span><input class="control" value="${escapeHtml(destination)}" placeholder="Select a language" readonly aria-readonly="true"></label></div><div class="brand-clone-footer"><p id="book-clone-feedback" class="brand-clone-feedback ${feedbackError ? "is-error" : ""}" role="${feedbackError ? "alert" : "status"}" aria-live="polite">${escapeHtml(feedback)}</p><div class="brand-clone-actions"><button class="button-secondary" type="button" data-action="cancel-book-clone">Cancel</button><button class="button-primary" type="button" data-action="submit-book-clone" aria-busy="${busy}" ${blocked ? "disabled" : ""}>${busy ? "Cloning…" : "Clone Book"}</button></div></div></fieldset></div>`;
  };

  const renderBookDetail = (book, summary) => {
    if (!state.bookDrawerOpen || !book || !summary) {
      return `<section class="panel book-detail-panel book-detail-empty" aria-labelledby="book-detail-empty-title"><div><h2 id="book-detail-empty-title">Select a Book</h2><p>Choose a Book from the list to review its details and production settings.</p></div></section>`;
    }
    const cover = assetForReference(summary, valueFor(summary, "representativeCoverReference", ""));
    const dirty = hasInteriorDraft(bookId(book));
    const saveDisabled = !workspaceStateAvailable(summary) || !dirty || processIsActive() || state.bookInteriorSavePending;
    const metadataDirty = hasMetadataDraft(book, summary);
    const keywordDirty = hasKeywordBuilderDraft(book, summary);
    const displayTitle = bookDisplayTitle(book, summary);
    const folderName = valueFor(book, "name", bookId(book));
    const cloneBusy = state.bookClonePending || state.bookCloneAwaitingSnapshot;
    const cloneDisabled = cloneBusy || processIsActive() || productionActionActive() || state.cacheCleanupActive || applicationIsLoading();
    return `<section class="panel book-detail-panel" aria-labelledby="book-detail-title"><header class="book-detail-header"><span class="book-detail-preview">${localImageMarkup(cover, `Cover for ${displayTitle}`)}</span><div class="book-detail-heading"><div class="book-detail-title-line"><h2 id="book-detail-title" tabindex="-1" title="${escapeHtml(displayTitle)}">${escapeHtml(displayTitle)}</h2><p class="book-folder-name" title="${escapeHtml(folderName)}">${escapeHtml(folderName)}</p></div><div class="book-detail-badges">${badge(productionStatus(summary, book))} ${badge(bookFrameState(summary))} <span data-book-assigned-brand-badge>${badge(assignedBrandName(summary) || "Unassigned")}</span></div></div><div class="book-detail-actions"><span data-book-interior-unsaved role="status" ${dirty || metadataDirty || keywordDirty ? "" : "hidden"}>Unsaved changes</span><div><button class="button-secondary" type="button" data-action="open-book-clone" data-book-id="${escapeHtml(bookId(book))}" aria-expanded="${state.bookCloneOpen && state.bookCloneSourceId === bookId(book)}" ${cloneDisabled ? "disabled" : ""}>Clone Book</button><button class="button-primary" data-action="save-book-interior-settings" data-book-id="${escapeHtml(bookId(book))}" ${saveDisabled ? "disabled" : ""} aria-busy="${state.bookInteriorSavePending}">${state.bookInteriorSavePending ? "Saving…" : "Save Interior changes"}</button></div></div></header>${renderBookCloneGroup(book)}<div class="book-detail-body book-drawer-body">${renderBookTabs(book, summary)}</div></section>`;
  };

  const patchBookMetadataValidationUi = (book, summary, focusFirst = false) => {
    const metadataCard = document.querySelector("[data-book-information-card]");
    if (!metadataCard) return;
    const validation = metadataValidationFor(bookId(book));
    for (const field of metadataFieldOrder) {
      const errors = validation.errors.filter((error) => error.field === field);
      const control = metadataCard.querySelector(`[data-metadata-field="${field}"]`);
      const errorElement = document.getElementById(`book-${field}-errors`);
      control?.classList.toggle("control-invalid", errors.length > 0);
      control?.setAttribute("aria-invalid", String(errors.length > 0));
      if (errorElement) {
        errorElement.hidden = errors.length === 0;
        errorElement.innerHTML = errors.map((error) => escapeHtml(error.message)).join("<br>");
      }
    }

    const dirty = hasMetadataDraft(book, summary);
    const heading = metadataCard.querySelector(".catalog-card-heading");
    const dirtyBadge = heading?.querySelector(":scope > .status-badge");
    if (dirty && !dirtyBadge) heading?.insertAdjacentHTML("beforeend", '<span class="status-badge status-warn">Unsaved</span>');
    if (!dirty) dirtyBadge?.remove();
    const save = metadataCard.querySelector('[data-action="save-book-metadata"]');
    if (save) save.disabled = !dirty || catalogMutationBusy() || processIsActive();
    const feedback = metadataCard.querySelector('[data-catalog-feedback="metadata"]');
    if (feedback) {
      const visible = state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.metadata.save";
      feedback.textContent = visible ? state.catalogFeedback : "";
      feedback.classList.toggle("is-error", visible && state.catalogFeedbackError);
      feedback.setAttribute("role", visible && state.catalogFeedbackError ? "alert" : "status");
    }

    if (focusFirst && validation.errors.length) {
      const firstField = metadataFieldOrder.find((field) => validation.errors.some((error) => error.field === field));
      const firstControl = firstField ? metadataCard.querySelector(`[data-metadata-field="${firstField}"]`) : null;
      firstControl?.focus();
      firstControl?.scrollIntoView?.({ block: "nearest" });
    }
  };

  const refreshBookKeywordBuilderCard = (focusSource = false) => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const card = document.querySelector("[data-book-keyword-builder-card]");
    if (!book || !summary || !card || state.selectedBookTab !== "asin") return;
    const drawerBody = document.querySelector(".book-drawer-body");
    const scrollTop = drawerBody?.scrollTop ?? state.bookDrawerScrollTop;
    const active = card.contains(document.activeElement) ? document.activeElement : null;
    const action = active?.dataset?.action ?? "";
    const selectionStart = active?.selectionStart;
    const selectionEnd = active?.selectionEnd;
    card.outerHTML = renderBookKeywordBuilder(book, summary);
    if (drawerBody) drawerBody.scrollTop = scrollTop;
    const next = focusSource
      ? document.querySelector('[data-action="book-keyword-source"]')
      : action ? document.querySelector(`[data-action="${CSS.escape(action)}"]`) : null;
    next?.focus?.();
    if (!focusSource && Number.isInteger(selectionStart) && next?.setSelectionRange) next.setSelectionRange(selectionStart, selectionEnd);
  };

  const patchAsinResearch = (id) => {
    if (!state.bookDrawerOpen || state.selectedBookTab !== "asin" || state.selectedBookId !== id) return;
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    const section = document.querySelector("[data-asin-research]");
    if (!book || !summary || !section) return;
    const active = section.contains(document.activeElement) ? document.activeElement : null;
    const action = active?.dataset?.action ?? "";
    const selectionStart = active?.selectionStart;
    const selectionEnd = active?.selectionEnd;
    const resultList = section.querySelector(".asin-result-list");
    const resultScrollTop = resultList?.scrollTop ?? 0;
    const resultScrollLeft = resultList?.scrollLeft ?? 0;
    const template = document.createElement("template");
    template.innerHTML = renderAsinResearch(book, summary);
    const rendered = template.content.firstElementChild;
    if (!rendered) return;
    const syncAttributes = (selector, names) => {
      const current = section.querySelector(selector);
      const next = rendered.querySelector(selector);
      if (!current || !next) return;
      names.forEach((name) => {
        const value = next.getAttribute(name);
        if (value === null) current.removeAttribute(name); else current.setAttribute(name, value);
      });
    };
    const syncContent = (selector) => {
      const current = section.querySelector(selector);
      const next = rendered.querySelector(selector);
      if (current && next) current.innerHTML = next.innerHTML;
    };
    section.setAttribute("aria-busy", rendered.getAttribute("aria-busy") ?? "false");
    syncContent("[data-asin-status]");
    syncAttributes("[data-asin-status]", ["class"]);
    syncContent("[data-asin-keyword-count]");
    syncContent(".asin-keyword-source");
    syncContent(".asin-research-actions");
    syncContent(".asin-research-state");
    syncAttributes(".asin-research-state", ["role"]);
    syncContent(".asin-progress-slot");
    syncContent(".asin-result-summary");
    syncContent(".asin-result-list");
    syncContent(".asin-stale-slot");
    syncContent("[data-asin-feedback]");
    syncAttributes("[data-asin-feedback]", ["class", "role"]);
    const refreshedResultList = section.querySelector(".asin-result-list");
    if (refreshedResultList) {
      refreshedResultList.scrollTop = resultScrollTop;
      refreshedResultList.scrollLeft = resultScrollLeft;
    }
    const next = action ? document.querySelector(`[data-asin-research] [data-action="${CSS.escape(action)}"]`) : null;
    next?.focus?.();
    if (Number.isInteger(selectionStart) && next?.setSelectionRange && !next.disabled) next.setSelectionRange(selectionStart, selectionEnd);
  };

  const updateBookCatalogMutationUi = () => {
    if (!state.bookDrawerOpen || currentRoute() !== "books") return;
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if (!book || !summary) return;
    const busy = catalogMutationBusy() || processIsActive();
    const metadataCard = document.querySelector("[data-book-information-card]");
    const metadataSave = metadataCard?.querySelector('[data-action="save-book-metadata"]');
    metadataCard?.querySelectorAll("input, textarea").forEach((control) => { control.disabled = busy; });
    metadataCard?.setAttribute("aria-busy", String(state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save"));
    if (metadataSave) {
      metadataSave.disabled = busy || !hasMetadataDraft(book, summary);
      metadataSave.setAttribute("aria-busy", String(state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save"));
      metadataSave.textContent = state.catalogMutationPending && state.catalogMutationCommand === "book.metadata.save" ? "Saving…" : "Save Book Information";
    }
    patchBookMetadataValidationUi(book, summary);
    const assignmentCard = document.querySelector("[data-book-assignment-card]");
    const select = assignmentCard?.querySelector('[data-action="book-brand-select"]');
    if (select) select.disabled = busy || !valueFor(metadataFor(summary), "author", "");
    const assign = assignmentCard?.querySelector('[data-action="assign-book-brand"]');
    if (assign) {
      assign.disabled = busy || !select?.value || select.value === assignedBrandName(summary);
      assign.textContent = state.catalogMutationPending && state.catalogMutationCommand === "book.brand.assign" ? "Assigning…" : "Assign Brand";
    }
    const unassign = assignmentCard?.querySelector('[data-action="unassign-book-brand"]');
    if (unassign) unassign.disabled = busy || !assignedBrandName(summary);
    const assignmentFeedback = assignmentCard?.querySelector('[data-catalog-feedback="assignment"]');
    if (assignmentFeedback) {
      const visible = state.catalogMutationTarget === bookId(book) && ["book.brand.assign", "book.brand.unassign"].includes(state.catalogMutationCommand);
      assignmentFeedback.textContent = visible ? state.catalogFeedback : "";
      assignmentFeedback.classList.toggle("is-error", visible && state.catalogFeedbackError);
      assignmentFeedback.setAttribute("role", visible && state.catalogFeedbackError ? "alert" : "status");
    }
    const completionCard = document.querySelector("[data-book-completion-card]");
    const completionButton = completionCard?.querySelector('[data-action="set-book-completion"]');
    if (completionButton) {
      const completed = bookIsCompleted(summary);
      const pending = catalogMutationBusy() && state.catalogMutationCommand === "book.completion.set" && state.catalogMutationTarget === bookId(book);
      completionCard.setAttribute("aria-busy", String(pending));
      completionButton.disabled = busy;
      completionButton.setAttribute("aria-busy", String(pending));
      completionButton.textContent = pending ? "Saving…" : completed ? "Mark as incomplete" : "Mark as completed";
    }
    const completionFeedback = completionCard?.querySelector('[data-catalog-feedback="completion"]');
    if (completionFeedback) {
      const visible = state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.completion.set";
      completionFeedback.textContent = visible ? state.catalogFeedback : "";
      completionFeedback.classList.toggle("is-error", visible && state.catalogFeedbackError);
      completionFeedback.setAttribute("role", visible && state.catalogFeedbackError ? "alert" : "status");
    }
    refreshBookKeywordBuilderCard();
  };

  const refreshBookCatalogCards = (command) => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if (!book || !summary || !state.bookDrawerOpen || state.selectedBookTab !== "settings") return;
    const drawerBody = document.querySelector(".book-drawer-body");
    const scrollTop = drawerBody?.scrollTop ?? state.bookDrawerScrollTop;
    const syncAssignmentCard = () => {
      const card = document.querySelector("[data-book-assignment-card]");
      if (!card) return;
      const template = document.createElement("template");
      template.innerHTML = renderBookBrandAssignment(book, summary);
      const next = template.content.firstElementChild;
      const badgeCurrent = card.querySelector(".catalog-card-heading > .status-badge");
      const badgeNext = next.querySelector(".catalog-card-heading > .status-badge");
      if (badgeCurrent && badgeNext) { badgeCurrent.className = badgeNext.className; badgeCurrent.textContent = badgeNext.textContent; }
      const summaryCurrent = card.querySelectorAll(".catalog-assignment-summary dd");
      const summaryNext = next.querySelectorAll(".catalog-assignment-summary dd");
      summaryCurrent.forEach((item, index) => { item.textContent = summaryNext[index]?.textContent ?? ""; });
      const warningCurrent = card.querySelector(":scope > .catalog-warning");
      const warningNext = next.querySelector(":scope > .catalog-warning");
      if (warningCurrent && warningNext) warningCurrent.textContent = warningNext.textContent;
      else if (warningCurrent) warningCurrent.remove();
      else if (warningNext) card.querySelector(":scope > .field")?.before(warningNext.cloneNode(true));
      const selectCurrent = card.querySelector('[data-action="book-brand-select"]');
      const selectNext = next.querySelector('[data-action="book-brand-select"]');
      if (selectCurrent && selectNext) { selectCurrent.innerHTML = selectNext.innerHTML; selectCurrent.disabled = selectNext.disabled; }
      const helpCurrent = card.querySelector(":scope > .field small");
      const helpNext = next.querySelector(":scope > .field small");
      if (helpCurrent && helpNext) helpCurrent.textContent = helpNext.textContent;
    };
    if (command === "book.metadata.save") {
      const metadataCard = document.querySelector("[data-book-information-card]");
      const persisted = metadataValues(summary);
      metadataCard?.querySelectorAll("[data-metadata-field]").forEach((control) => { control.value = persisted[control.dataset.metadataField] ?? ""; });
      metadataCard?.querySelector(".catalog-card-heading > .status-badge")?.remove();
      syncAssignmentCard();
      const title = document.getElementById("book-detail-title");
      if (title) title.textContent = bookDisplayTitle(book, summary);
    } else if (["book.brand.assign", "book.brand.unassign"].includes(command)) {
      syncAssignmentCard();
    } else if (command === "book.completion.set") {
      const completionCard = document.querySelector("[data-book-completion-card]");
      if (completionCard) completionCard.outerHTML = renderBookCompletion(book, summary);
    }
    if (drawerBody) drawerBody.scrollTop = scrollTop;
    updateBookCatalogMutationUi();
  };

  const refreshBrandDependentBookUi = (command) => {
    const book = selectedBook();
    const summary = book ? summaryFor(book) : null;
    if (!book || !summary || !state.bookDrawerOpen) return;
    const drawerBody = document.querySelector(".book-drawer-body");
    const scrollTop = drawerBody?.scrollTop ?? state.bookDrawerScrollTop;
    const readiness = processingReadiness(book, summary);
    const processButton = document.querySelector('[data-action="queue-selected-book"]');
    if (processButton) {
      processButton.disabled = !readiness.ready;
      processButton.title = readiness.reason;
      processButton.setAttribute("aria-label", `Process Interior. ${readiness.reason}`);
    }
    const assignedBadge = document.querySelector("[data-book-assigned-brand-badge]");
    if (assignedBadge) assignedBadge.innerHTML = badge(assignedBrandName(summary) || "Unassigned");

    if (state.selectedBookTab === "settings") {
      refreshBookCatalogCards(command);
      const copyCard = document.querySelector("[data-brand-template-copy-card]");
      if (copyCard) copyCard.outerHTML = renderBrandTemplateCopyCard(book, summary);
    } else if (state.selectedBookTab === "production") {
      refreshProductionWorkspace();
    }
    if (drawerBody) drawerBody.scrollTop = scrollTop;
  };

  const matchesBookBrandFilter = (summary) => state.bookBrandFilter === "All" || (state.bookBrandFilter === "Unassigned" ? !assignedBrandName(summary) : assignedBrandName(summary) === state.bookBrandFilter);
  const matchesBookStatusFilter = (book, summary, filter = state.bookStatus) => filter === "All" || (filter === "Complete" ? bookIsCompleted(summary) : productionStatus(summary, book) === filter);
  const filteredBooks = () => books().filter((book) => {
    const summary = summaryFor(book);
    const metadata = metadataFor(summary);
    const searchable = `${bookDisplayTitle(book, summary)} ${valueFor(book, "name", "")} ${valueFor(metadata, "author", "")}`.toLocaleLowerCase();
    return searchable.includes(state.bookFilter.toLocaleLowerCase()) && matchesBookBrandFilter(summary) && matchesBookStatusFilter(book, summary);
  }).sort((left, right) => {
    const leftSummary = summaryFor(left);
    const rightSummary = summaryFor(right);
    if (state.bookSort === "name") return bookDisplayTitle(left, leftSummary).localeCompare(bookDisplayTitle(right, rightSummary));
    return new Date(valueFor(rightSummary, "lastRunAt", 0)).getTime() - new Date(valueFor(leftSummary, "lastRunAt", 0)).getTime();
  });

  const bookListRowMarkup = (item) => {
    const itemSummary = summaryFor(item);
    const id = bookId(item);
    const name = bookDisplayTitle(item, itemSummary);
    const stateAvailable = workspaceStateAvailable(itemSummary);
    const active = state.bookDrawerOpen && state.selectedBookId === id;
    return `<button type="button" class="book-list-row ${active ? "is-active" : ""}" data-book-card-id="${escapeHtml(id)}" data-action="open-book-detail" data-book-id="${escapeHtml(id)}" aria-current="${active ? "true" : "false"}" aria-label="View details for ${escapeHtml(name)}"><span class="book-list-thumbnail">${bookThumbnailMarkup(item, itemSummary)}</span><span class="book-list-copy"><strong title="${escapeHtml(name)}">${escapeHtml(name)}</strong><span class="book-list-status">${badge(productionStatus(itemSummary, item))}${stateAvailable ? "" : ` ${badge("State unavailable")}`}</span></span></button>`;
  };

  const refreshBookListRow = (id) => {
    const book = books().find((item) => bookId(item) === id);
    const row = document.querySelector(`[data-book-card-id="${CSS.escape(id)}"]`);
    if (book && row) row.outerHTML = bookListRowMarkup(book);
  };

  const renderBooks = () => {
    const existingDrawerBody = document.querySelector(".book-drawer-body");
    if (existingDrawerBody && Number.isFinite(existingDrawerBody.scrollTop)) state.bookDrawerScrollTop = existingDrawerBody.scrollTop;
    const existingArtworkGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (existingArtworkGrid && Number.isFinite(existingArtworkGrid.scrollTop)) state.artworkGridScrollTop = existingArtworkGrid.scrollTop;
    const allBooks = books();
    const brandScopedBooks = allBooks.filter((book) => matchesBookBrandFilter(summaryFor(book)));
    const statusCounts = bookStatuses.map((name) => ({ name, count: brandScopedBooks.filter((book) => matchesBookStatusFilter(book, summaryFor(book), name)).length }));
    const filtered = filteredBooks();
    const pageSize = 12;
    const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
    state.bookPage = Math.min(Math.max(1, state.bookPage), totalPages);
    const pageItems = filtered.slice((state.bookPage - 1) * pageSize, state.bookPage * pageSize);
    if (!pageItems.some((item) => bookId(item) === state.selectedBookId)) {
      state.selectedBookId = "";
      state.bookDrawerOpen = false;
      state.bookDrawerScrollTop = 0;
    }
    const start = filtered.length ? (state.bookPage - 1) * pageSize + 1 : 0;
    const end = Math.min(state.bookPage * pageSize, filtered.length);
    const selectedCount = state.selectedBookIds.size;
    const processLabel = selectedCount ? `Process Interior · ${selectedCount} selected` : "Process Interior";
    const brandFilterOptions = [{ value: "All", label: "All", count: allBooks.length }, { value: "Unassigned", label: "Unassigned", count: allBooks.filter((book) => !assignedBrandName(summaryFor(book))).length }, ...[...brands()].sort((left, right) => valueFor(left, "name", "").localeCompare(valueFor(right, "name", ""), undefined, { sensitivity: "base" })).map((brand) => { const name = valueFor(brand, "name", ""); return { value: name, label: name, count: allBooks.filter((book) => assignedBrandName(summaryFor(book)) === name).length }; })];
    const selected = selectedBook();
    const pagination = `<footer class="book-pagination" data-book-total-pages="${totalPages}"><span>${start}–${end} of ${filtered.length}</span><div><button class="button-secondary" data-action="book-page" data-book-page="first" ${state.bookPage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="book-page" data-book-page="previous" ${state.bookPage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.bookPage} of ${totalPages}</span><button class="button-secondary" data-action="book-page" data-book-page="next" ${state.bookPage === totalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="book-page" data-book-page="last" ${state.bookPage === totalPages ? "disabled" : ""}>Last</button></div></footer>`;
    const list = pageItems.length ? pageItems.map(bookListRowMarkup).join("") : `<div class="book-list-empty"><strong>No Books match this view.</strong><span>Adjust the search, Book Brand or status filter.</span></div>`;
    content.innerHTML = `<section class="book-library-page"><div class="page-header"><div><h1>Books</h1><p>Filter local Books, then select one to review its production workspace.</p></div><div class="page-actions">${refreshAction()}<button class="button-secondary" data-action="clear-cache" ${cacheCleanupBlocked() ? "disabled" : ""}>${state.cacheCleanupActive ? "Clearing…" : "Clear Cache"}</button><button class="button-secondary" data-action="validate-all">Validate all</button><button class="button-primary" data-action="go-process">${processLabel}</button></div></div><section class="book-toolbar"><label class="field book-search-field"><span>Search books</span><input class="control" data-action="filter-books" value="${escapeHtml(state.bookFilter)}" placeholder="Title, folder or Author"></label><label class="field"><span>Book Brand</span><select class="control" data-action="book-brand-filter">${brandFilterOptions.map(({ value, label, count }) => `<option value="${escapeHtml(value)}" ${state.bookBrandFilter === value ? "selected" : ""}>${escapeHtml(label)} (${count})</option>`).join("")}</select></label><label class="field"><span>Sort</span><select class="control" data-action="book-sort"><option value="activity" ${state.bookSort === "activity" ? "selected" : ""}>Last activity</option><option value="name" ${state.bookSort === "name" ? "selected" : ""}>Book title</option></select></label><label class="field book-status-filter"><span>Status</span><select class="control" data-action="book-status">${statusCounts.map(({ name, count }) => `<option value="${escapeHtml(name)}" ${state.bookStatus === name ? "selected" : ""}>${escapeHtml(name)} (${count})</option>`).join("")}</select></label></section><div class="book-master-detail"><section class="panel book-list-panel" aria-labelledby="book-list-title"><header class="book-panel-header"><div><h2 id="book-list-title">Book list</h2><p>${filtered.length} matching Book${filtered.length === 1 ? "" : "s"}</p></div></header><div class="book-list-scroll">${list}</div>${pagination}</section>${renderBookDetail(selected, selected ? summaryFor(selected) : null)}</div></section>`;
    const drawerBody = document.querySelector(".book-drawer-body");
    if (drawerBody && Number.isFinite(state.bookDrawerScrollTop)) drawerBody.scrollTop = state.bookDrawerScrollTop;
    const artworkGrid = document.querySelector(".interior-artwork-grid-scroll");
    if (artworkGrid && Number.isFinite(state.artworkGridScrollTop)) artworkGrid.scrollTop = state.artworkGridScrollTop;
  };

  const processControlIdentity = (element) => {
    if (!element?.dataset?.action || !element.closest?.(".process-page")) return null;
    return {
      action: element.dataset.action,
      bookId: element.dataset.bookId ?? "",
      page: element.dataset.processQueuePage ?? ""
    };
  };
  const preserveProcessWorkspaceUi = () => {
    const queueScroll = content.querySelector(".process-queue-grid-scroll");
    if (queueScroll) state.processQueueScrollTop = queueScroll.scrollTop;
    state.processFocusIdentity = processControlIdentity(document.activeElement);
  };
  const restoreProcessWorkspaceUi = () => {
    const queueScroll = content.querySelector(".process-queue-grid-scroll");
    if (queueScroll) queueScroll.scrollTop = state.processQueueScrollTop;
    const identity = state.processFocusIdentity;
    if (!identity) return;
    const focusTarget = [...document.querySelectorAll('[data-action]')].find((element) =>
      element.dataset.action === identity.action &&
      (element.dataset.bookId ?? "") === identity.bookId &&
      (element.dataset.processQueuePage ?? "") === identity.page);
    focusTarget?.focus?.({ preventScroll: true });
  };

  const renderProcess = (requestProcess = true) => {
    preserveProcessWorkspaceUi();
    const session = window.processSnapshot;
    const active = valueFor(session, "isActive", false);
    const cancelling = valueFor(session, "isCancelling", false);
    const queueLocked = active || cancelling;
    const sessionQueue = valueFor(session, "queue", []);
    const hasSession = active || cancelling || sessionQueue.length > 0;
    const terminal = hasSession && !active && !cancelling;
    const mode = hasSession ? processMode(session) : "interior-only";
    const productionSession = mode === "production-interior" || mode === "full-book";
    const sessionName = productionSession ? "Build Final Interior" : "Process Interior";
    const pendingQueue = [...state.selectedBookIds].map((id) => {
      const book = books().find((candidate) => bookId(candidate) === id);
      return { bookId: { value: id }, status: "Ready", detail: valueFor(book, "name", id) };
    });
    const selectionReadiness = selectedProcessingReadiness();
    const queue = queueLocked ? sessionQueue : pendingQueue;
    const queueCurrentBook = sessionQueue.find((entry) => ["Running", "Processing"].includes(displayStatus(valueFor(entry, "status", "")))) ?? sessionQueue[0];
    const currentBook = valueFor(valueFor(session, "currentBookId", {}), "value", valueFor(valueFor(queueCurrentBook, "bookId", {}), "value", terminal ? "Last session" : "No active Book"));
    const terminalStatuses = sessionQueue.map((entry) => displayStatus(valueFor(entry, "status", "Not started")));
    const derivedTerminalStep = terminalStatuses.includes("Failed")
      ? "Failed"
      : terminalStatuses.includes("Cancelled")
        ? "Cancelled"
        : "Completed";
    const rawCurrentStep = valueFor(session, "currentStep", null) || (terminal ? derivedTerminalStep : "Preparing");
    const currentStep = processStepLabel(mode, rawCurrentStep);
    const completed = valueFor(session, "pagesCompleted", 0);
    const total = valueFor(session, "pagesTotal", 0);
    const percent = total ? Math.min(100, Math.round((completed / total) * 100)) : 0;
    const stage = cancelling ? "Cancelling" : terminal ? derivedTerminalStep : currentStep;
    const stageModel = processStageModel(mode, rawCurrentStep, terminal ? derivedTerminalStep : "");
    const stages = stageModel.stages;
    const currentStageIndex = stageModel.index;
    const failureDetails = sessionQueue.filter((entry) => displayStatus(valueFor(entry, "status", "")) === "Failed" && valueFor(entry, "detail", null));
    const outcomeCopy = terminal
      ? derivedTerminalStep === "Completed"
        ? productionSession ? "Final Interior built successfully." : "Interior pages prepared. Existing PDF unchanged."
        : derivedTerminalStep === "Cancelled"
          ? productionSession ? "Final Interior build cancelled. Previous PDF kept." : "Processing cancelled. Processed previews were cleared; existing PDF was kept."
          : productionSession ? "Final Interior build failed. Previous PDF kept." : "Processing failed. Processed previews were cleared; existing PDF was kept."
      : "";
    const outcomeMarkup = outcomeCopy
      ? `<div class="process-session-outcome ${derivedTerminalStep === "Failed" ? "process-failure" : ""}" role="${derivedTerminalStep === "Failed" ? "alert" : "status"}" ${derivedTerminalStep === "Failed" ? 'tabindex="-1" data-process-failure-summary' : ""}><strong>${escapeHtml(outcomeCopy)}</strong></div>`
      : "";
    const queueTotalPages = Math.max(1, Math.ceil(queue.length / processQueuePageSize));
    state.processQueuePage = Math.min(Math.max(1, state.processQueuePage), queueTotalPages);
    const queueStart = (state.processQueuePage - 1) * processQueuePageSize;
    const queueItems = queue.slice(queueStart, queueStart + processQueuePageSize);
    const queueRangeStart = queue.length ? queueStart + 1 : 0;
    const queueRangeEnd = Math.min(queueStart + processQueuePageSize, queue.length);
    const completedBooks = sessionQueue.filter((entry) => displayStatus(valueFor(entry, "status", "")) === "Completed").length;
    const failedBooks = sessionQueue.filter((entry) => displayStatus(valueFor(entry, "status", "")) === "Failed").length;
    const renderQueueCard = (entry) => {
      const id = valueFor(valueFor(entry, "bookId", {}), "value", "");
      const book = books().find((candidate) => bookId(candidate) === id);
      const summary = book ? summaryFor(book) : null;
      const name = valueFor(book, "name", valueFor(entry, "detail", id));
      const totalPages = valueFor(summary, "interiorSourcePageCount", 0);
      const activePages = valueFor(summary, "activeInteriorSourcePageCount", totalPages);
      const entryStatus = queueLocked ? valueFor(entry, "status", "NotStarted") : "Ready";
      const details = totalPages ? `${activePages} / ${totalPages} Interior active` : queueLocked ? valueFor(entry, "detail", "Waiting") : "Ready to prepare Interior pages";
      const preview = book ? bookThumbnailMarkup(book, summary, "Cover unavailable") : `<span class="book-preview-fallback">Cover unavailable</span>`;
      const main = book
        ? `<button type="button" class="process-queue-card-main" data-action="select-book" data-book-id="${escapeHtml(id)}" aria-label="Open ${escapeHtml(name)}"><span class="process-queue-preview">${preview}</span><span class="process-queue-copy"><strong title="${escapeHtml(name)}">${escapeHtml(name)}</strong><small>${escapeHtml(details)}</small><span>${badge(entryStatus)}</span></span></button>`
        : `<div class="process-queue-card-main"><span class="process-queue-preview">${preview}</span><span class="process-queue-copy"><strong title="${escapeHtml(name)}">${escapeHtml(name)}</strong><small>${escapeHtml(details)}</small><span>${badge(entryStatus)}</span></span></div>`;
      const action = queueLocked
        ? `<span class="process-queue-locked">Queue locked</span>`
        : `<button class="button-secondary process-queue-remove" data-action="remove-process-queue-book" data-book-id="${escapeHtml(id)}" aria-label="Remove ${escapeHtml(name)} from selected queue">Remove</button>`;
      return `<article class="process-queue-card">${main}<footer>${action}</footer></article>`;
    };
    const queuePanel = `<section class="process-queue-workspace" aria-labelledby="selected-queue-title"><header class="process-queue-heading"><div><h2 id="selected-queue-title">Selected queue <span>${queue.length}</span></h2><p>${queueLocked ? `Queue is locked while ${sessionName} is running.` : "Review selected Books before preparing Interior pages."}</p></div><span class="process-queue-range">${queueRangeStart}–${queueRangeEnd} of ${queue.length}</span></header><div class="process-queue-grid-scroll"><div class="process-queue-grid">${queueItems.length ? queueItems.map(renderQueueCard).join("") : `<div class="process-queue-empty"><strong>No Books selected</strong><span>Select ready Books from the Books workspace, then return here to process them.</span><button class="button-secondary" data-action="go-books">Go to Books</button></div>`}</div></div><footer class="process-queue-pagination" data-process-queue-total-pages="${queueTotalPages}"><span>${queueRangeStart}–${queueRangeEnd} of ${queue.length}</span><div><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="first" ${state.processQueuePage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="previous" ${state.processQueuePage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.processQueuePage} of ${queueTotalPages}</span><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="next" ${state.processQueuePage === queueTotalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="process-queue-page" data-process-queue-page="last" ${state.processQueuePage === queueTotalPages ? "disabled" : ""}>Last</button></div></footer></section>`;
    const resolvedQueueBrand = queueLocked ? valueFor(session, "brandName", "Resolving") || "Resolving" : selectionReadiness.brandName || "—";
    const summaryPanel = `<section class="panel process-summary-panel"><div class="process-panel-heading"><div><h2 class="panel-title">Summary</h2><p>${terminal ? `Last ${sessionName} session` : queueLocked ? `Current ${sessionName} session` : "Books ready to process"} · Assigned Brand: ${escapeHtml(resolvedQueueBrand)}</p></div>${badge(stage)}</div><div class="process-summary-stats"><div><span>Selected queue</span><strong>${queueLocked ? sessionQueue.length : pendingQueue.length}</strong></div><div><span>Completed</span><strong>${completedBooks}</strong></div><div><span>Failed</span><strong>${failedBooks}</strong></div><div><span>Workers</span><strong>${valueFor(session, "workerLimit", 0) || "—"}</strong></div><div><span>Elapsed</span><strong>${elapsedTime(valueFor(session, "startedAt", null))}</strong></div><div><span>Progress</span><strong>${completed} / ${total || "?"}</strong></div></div><ol class="process-stages">${stages.map((item, index) => `<li class="${index < currentStageIndex ? "complete" : index === currentStageIndex && queueLocked ? "active" : ""}"><span>${index + 1}</span>${item}</li>`).join("")}</ol></section>`;
    const currentStageStatus = `${stage}. ${currentBook}. ${completed} of ${total || "unknown"} pages.`;
    const currentStagePanel = `<section class="panel process-current-stage-panel"><div class="process-panel-heading"><div><h2 class="panel-title">Current stage</h2><p>${queueLocked ? "Live progress for the active Book" : terminal ? "Final state of the last session" : "Start processing when the selected queue is ready"}</p></div></div><p class="sr-only" role="status" aria-live="polite" aria-atomic="true">${escapeHtml(currentStageStatus)}</p><div class="process-book"><strong>${escapeHtml(currentBook)}</strong><span>${escapeHtml(currentStep)}</span></div><div class="progress-track"><span style="width:${percent}%"></span></div><p class="progress-copy">${completed} / ${total || "?"} pages · ${valueFor(session, "workerLimit", 0) || "?"} workers</p>${outcomeMarkup}${!selectionReadiness.ready && state.selectedBookIds.size ? `<div class="process-failure" role="alert"><strong>${selectionReadiness.mixed ? "Selected queue contains multiple Brands" : "Selected Book needs review"}</strong><p>${escapeHtml(selectionReadiness.reason)}</p></div>` : ""}${failureDetails.length ? `<div class="process-failure" role="alert"><strong>Run needs review</strong>${failureDetails.map((entry) => `<p>${escapeHtml(valueFor(valueFor(entry, "bookId", {}), "value", ""))}: ${escapeHtml(valueFor(entry, "detail", ""))}</p>`).join("")}</div>` : ""}<div class="page-actions mt-4">${queueLocked ? "" : `<button class="button-primary" data-action="start-process" ${selectionReadiness.ready ? "" : "disabled"}>${terminal ? "Start New Interior Processing" : "Start Interior Processing"}</button>`}</div></section>`;
    const pageDescription = cancelling
      ? `Stopping ${sessionName} session…`
      : active
        ? productionSession ? "Building and publishing the Final Interior PDF." : "Prepare Interior pages for preview. This does not build or replace a PDF."
        : terminal
          ? `Last ${sessionName} session`
          : "Prepare Interior pages for preview. This does not build or replace a PDF.";
    content.innerHTML = `<section class="process-page" aria-busy="${queueLocked}"><div class="page-header"><div><h1>${escapeHtml(sessionName)}</h1><p>${escapeHtml(pageDescription)}</p></div>${active ? cancelling ? '<button class="button-danger" disabled>Stopping processing…</button>' : '<button class="button-danger" data-action="cancel-process">Cancel session</button>' : ""}</div><div class="process-workspace">${summaryPanel}${currentStagePanel}${queuePanel}</div></section>`;
    restoreProcessWorkspaceUi();
    if (requestProcess) send("process.get");
  };

  const renderOutputs = () => {
    const library = pdfLibraryBooks();
    const eligibleTotal = eligiblePdfLibraryBooks().length;
    const totalPages = Math.max(1, Math.ceil(library.length / pdfLibraryPageSize));
    state.pdfLibraryPage = Math.min(Math.max(1, state.pdfLibraryPage), totalPages);
    const pageStart = (state.pdfLibraryPage - 1) * pdfLibraryPageSize;
    const pageItems = library.slice(pageStart, pageStart + pdfLibraryPageSize);
    const start = library.length ? pageStart + 1 : 0;
    const end = Math.min(pageStart + pdfLibraryPageSize, library.length);
    const outputRow = (summary, output) => {
      const pageCount = valueFor(output, "pageCount", "—");
      const dimensions = valueFor(output, "widthInches", null) ? `${inches(valueFor(output, "widthInches", 0))} × ${inches(valueFor(output, "heightInches", 0))} in` : "—";
      const fileName = valueFor(output, "fileName", "PDF output");
      const kind = valueFor(output, "artifactKind", "Unknown");
      const verification = valueFor(output, "verificationStatus", "Available");
      const previewable = ["Verified", "Available"].includes(verification);
      const id = valueFor(valueFor(summary, "bookId", {}), "value", "");
      const artifactReference = valueFor(output, "artifactReference", "");
      const actionKey = `preview:${id}:${artifactReference}`;
      const pending = state.pdfLibraryPendingActions.has(actionKey);
      const statusMarkup = previewable ? "" : `<span class="pdf-library-file-status">${escapeHtml(displayStatus(verification))}</span>`;
      const pageLabel = pageCount === 1 ? "page" : "pages";
      return `<li class="pdf-library-file"><button type="button" class="pdf-library-file-button${previewable ? "" : " is-unavailable"}" data-action="preview-output" data-book-id="${escapeHtml(id)}" data-artifact-reference="${escapeHtml(artifactReference)}" data-output-action-key="${escapeHtml(actionKey)}" data-output-idle-label="Preview ›" data-output-busy-label="Opening…" title="${escapeHtml(fileName)}" aria-busy="${pending}" ${previewable && !pending ? "" : "disabled"}><span class="pb-visually-hidden">Preview </span><span class="pdf-library-file-copy"><span class="pdf-library-file-title"><strong>${escapeHtml(kind)}</strong>${statusMarkup}</span><span class="pdf-library-file-meta">${escapeHtml(String(pageCount))} ${pageLabel} · ${escapeHtml(dimensions)} · ${fileSize(valueFor(output, "fileSizeBytes", 0))}</span></span><span class="pdf-library-file-action" data-output-action-label>${pending ? "Opening…" : "Preview ›"}</span></button></li>`;
    };
    const bookCard = ({ book, summary }) => {
      const name = pdfLibraryBookName(book, summary);
      const thumbnail = pdfLibraryCoverThumbnailMarkup(book, summary, "Cover unavailable");
      const outputs = pdfLibraryOutputs(summary);
      const id = valueFor(valueFor(summary, "bookId", {}), "value", "");
      const actionKey = `folder:${id}`;
      const pending = state.pdfLibraryPendingActions.has(actionKey);
      const folderAvailable = outputs.some((output) => valueFor(output, "verificationStatus", "Missing") !== "Missing");
      return `<article class="pdf-library-book pdf-library-book-${state.pdfLibraryView}" data-pdf-book-id="${escapeHtml(name)}"><span class="pdf-library-book-preview">${thumbnail}</span><header class="pdf-library-book-header"><h2 title="${escapeHtml(name)}">${escapeHtml(name)}</h2></header><ul class="pdf-library-files">${outputs.map((output) => outputRow(summary, output)).join("")}</ul><footer class="pdf-library-book-footer"><button type="button" class="button-secondary pdf-library-open-folder" data-action="open-output-folder" data-book-id="${escapeHtml(id)}" data-output-action-key="${escapeHtml(actionKey)}" data-output-idle-label="Open Folder" data-output-busy-label="Opening…" aria-busy="${pending}" ${folderAvailable && !pending ? "" : "disabled"}><span data-output-action-label>${pending ? "Opening…" : "Open Folder"}</span></button></footer></article>`;
    };
    const empty = eligibleTotal === 0
      ? `<section class="pdf-library-empty"><strong>No completed PDFs yet.</strong><p>Build a Cover PDF or Final Interior PDF to make it appear here.</p></section>`
      : `<section class="pdf-library-empty"><strong>No PDF Books match your search.</strong><p>Try a different Book name.</p></section>`;
    const pagination = library.length ? `<footer class="book-pagination pdf-library-pagination" data-pdf-library-total-pages="${totalPages}"><span>${start}–${end} of ${library.length}</span><div><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="first" ${state.pdfLibraryPage === 1 ? "disabled" : ""}>First</button><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="previous" ${state.pdfLibraryPage === 1 ? "disabled" : ""}>Previous</button><span>Page ${state.pdfLibraryPage} of ${totalPages}</span><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="next" ${state.pdfLibraryPage === totalPages ? "disabled" : ""}>Next</button><button class="button-secondary" data-action="pdf-library-page" data-pdf-library-page="last" ${state.pdfLibraryPage === totalPages ? "disabled" : ""}>Last</button></div></footer>` : "";
    const results = library.length ? `<section class="${state.pdfLibraryView === "grid" ? "pdf-library-grid" : "pdf-library-list"}">${pageItems.map(bookCard).join("")}</section>` : empty;
    content.innerHTML = `<section class="pdf-library-page"><div class="page-header"><div><h1>PDF Library</h1><p>Select Cover or Interior to open it in your default PDF app.</p></div></div><div class="pdf-library-toolbar"><label class="field"><span>Search Books</span><input class="control" type="search" value="${escapeHtml(state.pdfLibrarySearch)}" placeholder="Search Books..." data-action="pdf-library-search"></label><label class="field pdf-library-sort"><span>Sort</span><select class="control" data-action="pdf-library-sort"><option value="newest" ${state.pdfLibrarySort === "newest" ? "selected" : ""}>Newest</option><option value="name" ${state.pdfLibrarySort === "name" ? "selected" : ""}>Name</option><option value="size" ${state.pdfLibrarySort === "size" ? "selected" : ""}>Size</option></select></label><div class="asset-view-toggle" aria-label="PDF Library view"><button class="${state.pdfLibraryView === "grid" ? "active" : ""}" data-action="pdf-library-view" data-pdf-library-view="grid" aria-pressed="${state.pdfLibraryView === "grid"}">Grid</button><button class="${state.pdfLibraryView === "list" ? "active" : ""}" data-action="pdf-library-view" data-pdf-library-view="list" aria-pressed="${state.pdfLibraryView === "list"}">List</button></div></div><p class="pdf-library-feedback ${state.pdfLibraryFeedbackError ? "is-error" : ""}" data-pdf-library-feedback role="${state.pdfLibraryFeedbackError ? "alert" : "status"}" ${state.pdfLibraryFeedback ? "" : "hidden"}>${escapeHtml(state.pdfLibraryFeedback)}</p><section class="pdf-library-results"><div class="pdf-library-grid-scroll">${results}</div>${pagination}</section></section>`;
    if (state.pdfLibrarySearchFocused) {
      const input = content.querySelector('[data-action="pdf-library-search"]');
      if (input) {
        input.focus();
        input.setSelectionRange?.(state.pdfLibrarySearchCaret, state.pdfLibrarySearchCaret);
      }
    }
  };

  const diagnosticsTabValue = (value) => ["summary", "tasks", "performance", "book"].includes(value) ? value : "summary";

  const renderDiagnosticsTabs = () => {
    const tabs = [["summary", "Summary"], ["tasks", "Tasks"], ["performance", "Performance"], ["book", "Book"]];
    return `<nav class="diagnostics-tabs" role="tablist" aria-label="Diagnostics views">${tabs.map(([value, label]) => `<button type="button" role="tab" class="${state.diagnosticsTab === value ? "active" : ""}" data-action="diagnostics-tab" data-diagnostics-tab="${value}" aria-selected="${state.diagnosticsTab === value}">${label}</button>`).join("")}</nav>`;
  };

  const diagnosticActiveTasks = () => state.backgroundTasks.filter((task) => ["Queued", "Running", "Cancelling"].includes(valueFor(task, "state", "")));
  const diagnosticFailedTasks = () => state.backgroundTasks.filter((task) => valueFor(task, "state", "") === "Failed");
  const diagnosticEvents = () => valueFor(window, "uiDiagnostics", []);
  const diagnosticPerformanceEvents = () => diagnosticEvents().filter((item) => (Number(valueFor(item, "durationMilliseconds", 0)) || 0) > 0 || (Number(valueFor(item, "severity", 0)) || 0) > 0);
  const diagnosticSlowEvents = () => diagnosticPerformanceEvents().filter((item) => (Number(valueFor(item, "severity", 0)) || 0) > 0);
  const diagnosticWorstDuration = () => diagnosticSlowEvents().reduce((worst, item) => Math.max(worst, Number(valueFor(item, "durationMilliseconds", 0)) || 0), 0);
  const diagnosticMissingFolders = (summary) => valueFor(summary, "sourceFolders", []).filter((folder) => valueFor(folder, "status", "Missing") !== "Present");
  const diagnosticRuntimeHealth = () => {
    const failed = diagnosticFailedTasks().length;
    const active = diagnosticActiveTasks().length;
    if (failed) return { label: "Needs attention", tone: "bad", detail: `${failed} failed task${failed === 1 ? "" : "s"}` };
    if (active) return { label: "Active", tone: "warn", detail: `${active} task${active === 1 ? "" : "s"} running` };
    return { label: "Healthy", tone: "good", detail: "No failed tasks" };
  };
  const diagnosticUiHealth = () => {
    const slow = diagnosticSlowEvents().length;
    return slow ? { label: "Needs review", tone: "warn", detail: `${slow} slow operation${slow === 1 ? "" : "s"}` } : { label: "Healthy", tone: "good", detail: "No slow operations" };
  };
  const diagnosticLatestSlowEvent = () => [...diagnosticSlowEvents()].sort((left, right) => new Date(valueFor(right, "timestamp", 0)).getTime() - new Date(valueFor(left, "timestamp", 0)).getTime())[0] ?? null;
  const diagnosticAttentionItems = (summary) => {
    const items = [];
    const failed = diagnosticFailedTasks();
    if (failed.length) items.push({ tone: "bad", title: `${failed.length} failed background task${failed.length === 1 ? "" : "s"}`, detail: "Open Tasks for details." });
    const slow = diagnosticSlowEvents();
    if (slow.length) items.push({ tone: "warn", title: `${slow.length} slow UI operation${slow.length === 1 ? "" : "s"}`, detail: `Worst ${diagnosticWorstDuration()} ms.` });
    if (summary) {
      const workspace = workspaceStatus(summary);
      if (["Failed", "Interrupted", "Cancelled"].includes(workspace)) items.push({ tone: "bad", title: `Selected Book is ${workspace}`, detail: "Open Book diagnostics for workspace details." });
      const missing = diagnosticMissingFolders(summary);
      if (missing.length) items.push({ tone: "warn", title: `${missing.length} source folder${missing.length === 1 ? "" : "s"} unavailable`, detail: missing.map((folder) => valueFor(folder, "name", "Unknown folder")).join(", ") });
    }
    return items;
  };
  const diagnosticRecentTasks = () => [...state.backgroundTasks].sort((left, right) => new Date(valueFor(right, "finishedAt", null) ?? valueFor(right, "startedAt", 0)).getTime() - new Date(valueFor(left, "finishedAt", null) ?? valueFor(left, "startedAt", 0)).getTime()).slice(0, 5);
  const renderDiagnosticHealthCard = (title, value, detail, tone) => `<article class="diagnostic-health-card"><span>${escapeHtml(title)}</span><strong class="diagnostic-health-${tone}">${escapeHtml(value)}</strong><small>${escapeHtml(detail)}</small></article>`;
  const renderDiagnosticsAttention = (items) => `<section class="panel diagnostic-attention"><h2 class="panel-title">Needs attention</h2>${items.length ? `<ul class="diagnostic-attention-list">${items.map((item) => `<li class="diagnostic-attention-${item.tone}"><strong>${escapeHtml(item.title)}</strong><span>${escapeHtml(item.detail)}</span></li>`).join("")}</ul>` : "<p class=\"empty-copy\">No diagnostic issues detected.</p>"}</section>`;
  const renderDiagnosticsRecentActivity = (tasks) => `<section class="panel diagnostic-recent"><h2 class="panel-title">Recent activity</h2>${tasks.length ? `<ul class="diagnostic-recent-list">${tasks.map((task) => `<li><strong>${escapeHtml(valueFor(task, "kind", "Task"))}</strong>${badge(valueFor(task, "state", "Unknown"))}<span>${escapeHtml(valueFor(task, "subject", "—"))}</span><time>${dateTime(valueFor(task, "finishedAt", null) ?? valueFor(task, "startedAt", null))}</time></li>`).join("")}</ul>` : "<p class=\"empty-copy\">No retained background activity.</p>"}</section>`;
  const renderDiagnosticsSummary = (book, summary) => {
    const runtime = diagnosticRuntimeHealth();
    const ui = diagnosticUiHealth();
    const missing = summary ? diagnosticMissingFolders(summary) : [];
    const bookName = book ? valueFor(book, "name", bookId(book)) : "No Book selected";
    const bookState = summary ? workspaceStatus(summary) : "Not selected";
    const bookTone = ["Failed", "Interrupted", "Cancelled"].includes(bookState) ? "bad" : bookState === "Running" ? "warn" : "good";
    return `<section role="tabpanel" data-diagnostics-panel="summary"><div class="diagnostic-health-grid">${renderDiagnosticHealthCard("Runtime", runtime.label, runtime.detail, runtime.tone)}${renderDiagnosticHealthCard("UI health", ui.label, ui.detail, ui.tone)}${renderDiagnosticHealthCard("Selected Book", bookState, bookName, bookTone)}${renderDiagnosticHealthCard("Source files", summary ? (missing.length ? `${missing.length} missing` : "All present") : "No Book selected", summary ? `${valueFor(summary, "sourceFolders", []).length} tracked folders` : "", missing.length ? "warn" : "good")}</div>${renderDiagnosticsAttention(diagnosticAttentionItems(summary))}${renderDiagnosticsRecentActivity(diagnosticRecentTasks())}</section>`;
  };
  const diagnosticTaskRows = () => state.backgroundTasks.slice(0, 20).map((task) => `<tr><td>${escapeHtml(valueFor(task, "kind", ""))}</td><td>${badge(valueFor(task, "state", "Unknown"))}</td><td>${escapeHtml(valueFor(task, "subject", "—"))}</td><td>${escapeHtml(valueFor(task, "step", "—"))}</td><td>${valueFor(task, "completed", "—")}/${valueFor(task, "total", "—")}</td><td>${dateTime(valueFor(task, "startedAt", null))}</td><td>${dateTime(valueFor(task, "finishedAt", null))}</td><td>${escapeHtml(valueFor(task, "errorMessage", "—"))}</td></tr>`).join("") || "<tr><td colspan=\"8\" class=\"empty-row\">No retained background tasks.</td></tr>";
  const renderDiagnosticsTasks = () => `<section role="tabpanel" data-diagnostics-panel="tasks"><div class="diagnostic-detail-strip"><div><span>Active</span><strong>${diagnosticActiveTasks().length}</strong></div><div><span>Failed</span><strong>${diagnosticFailedTasks().length}</strong></div><div><span>Retained</span><strong>${state.backgroundTasks.length}</strong></div></div>${panel("Background workers", `<div class="table-scroll"><table class="data-table"><thead><tr><th>Kind</th><th>State</th><th>Subject</th><th>Step</th><th>Progress</th><th>Started</th><th>Finished</th><th>Error</th></tr></thead><tbody>${diagnosticTaskRows()}</tbody></table></div>`)}</section>`;
  const diagnosticPerformanceRows = () => {
    const events = diagnosticPerformanceEvents();
    if (!events.length) return "<tr><td colspan=\"7\" class=\"empty-row\">No meaningful UI performance operations recorded.</td></tr>";
    return events.map((item) => `<tr><td>${dateTime(valueFor(item, "timestamp", null))}</td><td>${escapeHtml(valueFor(item, "severity", "Info"))}</td><td>${escapeHtml(valueFor(item, "kind", "operation"))}</td><td>${escapeHtml(valueFor(item, "operation", ""))}</td><td>${Number(valueFor(item, "durationMilliseconds", 0)) || 0} ms</td><td>${escapeHtml(valueFor(item, "subject", "—") || "—")}</td><td>${escapeHtml(valueFor(item, "activeOperations", []).join(", ") || "—")}</td></tr>`).join("");
  };
  const renderDiagnosticsPerformance = () => {
    const slow = diagnosticSlowEvents();
    const worst = diagnosticWorstDuration();
    const latest = diagnosticLatestSlowEvent();
    return `<section role="tabpanel" data-diagnostics-panel="performance"><div class="diagnostic-detail-strip"><div><span>Slow operations</span><strong>${slow.length}</strong></div><div><span>Worst duration</span><strong>${worst ? `${worst} ms` : "—"}</strong></div><div><span>Latest slow operation</span><strong>${escapeHtml(latest ? valueFor(latest, "operation", "—") : "—")}</strong></div></div>${panel("UI responsiveness", `<div class="table-scroll"><table class="data-table"><thead><tr><th>Time</th><th>Severity</th><th>Kind</th><th>Operation</th><th>Duration</th><th>Subject</th><th>Active during stall</th></tr></thead><tbody>${diagnosticPerformanceRows()}</tbody></table></div>`)}</section>`;
  };
  const diagnosticLogText = (log) => [String(valueFor(log, "eventName", "") ?? "").trim(), String(valueFor(log, "detail", "") ?? "").trim()].filter(Boolean).join(" · ");
  const diagnosticMeaningfulLogs = (summary) => valueFor(summary, "logs", []).filter((log) => { const text = diagnosticLogText(log); return text && text !== "."; }).slice(-12).reverse();
  const renderDiagnosticsBook = (book, summary) => {
    const selectedId = state.selectedBookId || bookId(book);
    const selector = `<label class="field diagnostic-book-field"><span>Book</span><select class="control diagnostic-select" data-action="diagnostic-book">${books().map((item) => `<option value="${escapeHtml(bookId(item))}" ${bookId(item) === selectedId ? "selected" : ""}>${escapeHtml(valueFor(item, "name", bookId(item)))}</option>`).join("")}</select></label>`;
    if (!book || !summary) return `<section role="tabpanel" data-diagnostics-panel="book"><div class="diagnostic-book-toolbar">${selector}</div>${panel("Book diagnostics", "<p class=\"empty-copy\">Select a Book to inspect its workspace.</p>")}</section>`;
    const folders = valueFor(summary, "sourceFolders", []);
    const logs = diagnosticMeaningfulLogs(summary);
    return `<section role="tabpanel" data-diagnostics-panel="book"><div class="diagnostic-book-toolbar"><div><strong>${escapeHtml(valueFor(book, "name", bookId(book)))}</strong><span>Workspace and source diagnostics</span></div>${selector}</div><div class="diagnostics-grid">${panel("Workspace", `<dl class="path-grid"><div><dt>Workspace state</dt><dd>${badge(workspaceStatus(summary))}</dd></div><div><dt>Current step</dt><dd>${escapeHtml(valueFor(summary, "currentStep", null) || "Not started")}</dd></div><div><dt>Last run</dt><dd>${dateTime(valueFor(summary, "lastRunAt", null))}</dd></div></dl>`)}${panel("Source folders", `<div class="table-scroll"><table class="data-table"><thead><tr><th>Folder</th><th>Status</th><th>Images</th></tr></thead><tbody>${folders.length ? folders.map((folder) => `<tr><td>${escapeHtml(valueFor(folder, "name", ""))}</td><td>${badge(valueFor(folder, "status", "Missing"))}</td><td>${valueFor(folder, "imageCount", 0)}</td></tr>`).join("") : "<tr><td colspan=\"3\" class=\"empty-row\">No source folders recorded.</td></tr>"}</tbody></table></div>`)}</div>${panel("Recent logs", logs.length ? `<ul class="log-list diagnostic-log-list">${logs.map((log) => `<li><time>${dateTime(valueFor(log, "timestamp", null))}</time><span>${escapeHtml(diagnosticLogText(log))}</span></li>`).join("")}</ul>` : "<p class=\"empty-copy\">No meaningful Book logs recorded.</p>", "mt-5")}</section>`;
  };
  const renderDiagnosticsPanel = (book, summary) => {
    if (state.diagnosticsTab === "tasks") return renderDiagnosticsTasks();
    if (state.diagnosticsTab === "performance") return renderDiagnosticsPerformance();
    if (state.diagnosticsTab === "book") return renderDiagnosticsBook(book, summary);
    return renderDiagnosticsSummary(book, summary);
  };
  const renderDiagnostics = () => {
    const book = selectedBook() ?? books()[0] ?? null;
    const summary = book ? summaryFor(book) : null;
    content.innerHTML = `<div class="page-header"><div><h1>Diagnostics</h1><p>Inspect application health and Book workspace details.</p></div><div class="page-actions"><button class="button-secondary" data-action="refresh-diagnostics">Refresh diagnostics</button></div></div>${renderDiagnosticsTabs()}${renderDiagnosticsPanel(book, summary)}`;
  };

  const render = (route, requestProcess = true) => {
    updateGlobalRefreshControl();
    document.querySelectorAll("[data-route]").forEach((button) => button.classList.toggle("nav-item-active", button.dataset.route === route));
    const subtitle = document.getElementById("page-subtitle");
    if (subtitle) subtitle.textContent = `${routeNames[route] ?? "Application"} workspace`;
    if (!window.appSnapshot) {
      content.innerHTML = state.applicationLoadState === "failed"
        ? renderLoadFailure()
        : panel("Loading library…", "<p class=\"panel-note\">Discovering Books, workspace state and local outputs.</p>");
      return;
    }
    if (route === "configuration") renderConfiguration();
    if (route === "brands") renderBrands();
    if (route === "books") renderBooks();
    if (route === "process") renderProcess(requestProcess);
    if (route === "outputs") renderOutputs();
    if (route === "diagnostics") renderDiagnostics();
    if (state.applicationLoadState === "failed") content.insertAdjacentHTML("afterbegin", renderRefreshFailure());
  };

  document.querySelectorAll("[data-route]").forEach((button) => button.addEventListener("click", () => { render(button.dataset.route); if (button.dataset.route === "configuration") loadStorage(); if (button.dataset.route === "diagnostics") { send("diagnostics.get"); send("task.list"); } }));
  const setBookListRowActive = (row, active) => {
    if (!row) return;
    row.classList?.toggle("is-active", active);
    row.setAttribute?.("aria-current", String(active));
  };
  const openBookDetail = (id, sourceRow = null) => {
    if (state.bookDrawerOpen && state.selectedBookId === id) {
      return;
    }
    if (state.bookCloneOpen && state.bookCloneSourceId !== id) resetBookClone();
    const previousBookId = state.selectedBookId;
    state.selectedBookId = id;
    if (previousBookId !== id) state.amazonBrowserStatus = { state: "Closed", reasonCode: null };
    state.selectedBookTab = "settings";
    state.selectedAssetReference = "";
    clearArtworkBulkSelection();
    state.assetStatus = "Active";
    state.assetFrameMode = "";
    state.introTemplatePage = 1;
    state.bookDrawerScrollTop = 0;
    state.artworkGridScrollTop = 0;
    state.bookDrawerOpen = true;
    const book = selectedBook();
    if (!book) return;
    const summary = summaryFor(book);
    const previousRow = previousBookId && previousBookId !== id
      ? document.querySelector(`[data-book-card-id="${CSS.escape(previousBookId)}"]`)
      : null;
    const selectedRow = sourceRow ?? document.querySelector(`[data-book-card-id="${CSS.escape(id)}"]`);
    setBookListRowActive(previousRow, false);
    setBookListRowActive(selectedRow, true);
    const detailPanel = document.querySelector(".book-detail-panel");
    if (detailPanel) detailPanel.outerHTML = renderBookDetail(book, summary);
    else render("books", false);
    send("amazon.browser.status", { bookId: id });
    send("book.keywords.asin-crawl.get", { bookId: id });
    loadStorage();
  };
  const beginCatalogMutation = (command, target, payload) => {
    if (state.catalogMutationPending || processIsActive()) return;
    state.catalogMutationPending = true;
    state.catalogMutationAwaitingSnapshot = false;
    state.catalogMutationCommand = command;
    state.catalogMutationTarget = target;
    state.catalogFeedback = "Saving…";
    state.catalogFeedbackError = false;
    send(command, payload);
    if (currentRoute() === "books" && state.bookDrawerOpen) updateBookCatalogMutationUi();
    if (currentRoute() === "brands") render("brands", false);
  };
  const catalogErrorMessage = (code) => ({ invalid_book_completion: "The completion request is invalid. Refresh and retry.", invalid_book_metadata: "Book Information is invalid. Review Title, Subtitle, Subcover, ASIN, and Author, then retry.", invalid_keyword_builder: "Keyword Builder input is invalid.", keyword_preview_invalid: "This preview is invalid or expired. Shuffle again.", keyword_preview_stale: "Inputs changed after this preview. Shuffle again.", keyword_preview_version_unsupported: "This saved output uses an older shuffle version. Shuffle again.", keyword_legacy_shuffle_required: "Shuffle once to update this legacy keyword output.", keyword_word_too_long: "A keyword word is longer than 50 characters. Shorten it and retry.", keyword_capacity_exceeded: "The ordered keyword stream needs more than seven fields. Remove, shorten, or reorder source keywords.", invalid_brand_author: "Brand Author must be a single line.", book_author_required: "Save a Book Author before assigning a Brand.", brand_author_required: "The selected Brand does not have an Author.", book_brand_language_mismatch: "Book Language must match Brand Language before assignment.", book_brand_author_mismatch: "Book Author must match Brand Author before assignment.", brand_metadata_invalid: "Brand metadata could not be read. Fix the metadata file and retry.", processing_active: "Interior Processing is running. Try again when it finishes.", production_action_active: "A Production action is running. Try again when it finishes.", cache_cleanup_active: "Cache Cleanup is running. Try again when it finishes.", snapshot_unavailable: "The library snapshot is unavailable. Refresh and retry.", book_not_found: "This Book is no longer available. Refresh the library.", brand_not_found: "This Brand is no longer available. Refresh the library." })[code] ?? "The change could not be saved. Refresh and retry.";
  document.addEventListener("keydown", (event) => {
    const activeTab = event.target.closest?.('[role="tab"][data-action="book-tab"]');
    if (activeTab && ["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
      const tabs = [...activeTab.closest('[role="tablist"]')?.querySelectorAll('[role="tab"][data-action="book-tab"]') ?? []];
      if (tabs.length) {
        event.preventDefault();
        const current = tabs.indexOf(activeTab);
        const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (current + (event.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
        tabs[next].click();
      }
      return;
    }
  });
  content.addEventListener("click", (event) => {
    const updateAction = event.target.closest("[data-update-action]")?.dataset.updateAction;
    if (updateAction) { beginUpdateAction(updateAction); return; }
    const target = event.target.closest("[data-action]");
    if (!target) return;
    const action = target.dataset.action;
    if (action === "storage-open-url") {
      const url = String(target.dataset.storageUrl ?? "");
      if (url) window.open(url, "_blank", "noopener,noreferrer");
      return;
    }
    if (action === "storage-copy-url") {
      const url = String(target.dataset.storageUrl ?? "");
      if (url && navigator.clipboard?.writeText) navigator.clipboard.writeText(url).then(() => { status.textContent = "S3 URL copied"; }, () => { status.textContent = "S3 URL could not be copied"; });
      return;
    }
    if (["storage-check", "storage-upload", "storage-cancel"].includes(action)) {
      const id = target.dataset.bookId;
      state.storagePendingBooks.add(id);
      const command = action === "storage-check" ? "book.s3.check" : action === "storage-upload" ? "book.s3.upload" : "book.s3.cancel";
      const requestId = send(command, { bookId: id });
      state.storageRequestBooks.set(requestId, id);
      patchBookS3(id);
      return;
    }
    if (action === "replace-s3-credentials") {
      const accessKey = String(document.querySelector("[data-s3-access-key]")?.value ?? "").trim();
      const secretKey = String(document.querySelector("[data-s3-secret-key]")?.value ?? "").trim();
      state.storageSettingsPending = true;
      state.storageFeedback = "Replacing credentials…";
      state.storageFeedbackError = false;
      send("s3.credentials.replace", { accessKey, secretKey });
      renderConfiguration();
      return;
    }
    if (action === "diagnostics-tab") {
      state.diagnosticsTab = diagnosticsTabValue(target.dataset.diagnosticsTab);
      render("diagnostics", false);
      return;
    }
    if (action === "refresh" || action === "validate-all") {
      if (action === "refresh" && currentRoute() === "configuration") {
        state.settingsFeedback = "";
        state.settingsFeedbackError = false;
      }
      beginApplicationRefresh();
    }
    if (action === "refresh-diagnostics") { send("diagnostics.get"); send("task.list"); }
    if (action === "select-brand") { resetBrandClone(); state.inspectedBrand = target.dataset.brandName; state.brandValidationResult = null; render("brands"); }
    if (action === "open-brand-clone") { state.brandCloneOpen = true; state.brandCloneFeedback = ""; state.brandCloneFeedbackError = false; state.brandCloneNotice = ""; render("brands", false); }
    if (action === "cancel-brand-clone") { resetBrandClone(); render("brands", false); }
    if (action === "submit-brand-clone") {
      const destinationBrandName = brandCloneDestinationName(state.inspectedBrand, state.brandCloneLanguageCode);
      const destinationExists = Boolean(destinationBrandName) && brands().some((brand) => String(valueFor(brand, "name", "")).toLocaleLowerCase() === destinationBrandName.toLocaleLowerCase());
      if (!destinationBrandName || destinationExists || state.brandClonePending || state.brandCloneAwaitingSnapshot || processIsActive()) return;
      state.brandClonePending = true;
      state.brandCloneDestination = destinationBrandName;
      state.brandCloneFeedback = "Cloning Brand…";
      state.brandCloneFeedbackError = false;
      send("brand.clone", { brandName: state.inspectedBrand, languageCode: state.brandCloneLanguageCode });
      render("brands", false);
    }
    if (action === "open-book-clone") {
      resetBookClone();
      state.bookCloneOpen = true;
      state.bookCloneSourceId = target.dataset.bookId;
      render("books", false);
      window.requestAnimationFrame?.(() => document.querySelector('[data-action="clone-book-language"]')?.focus?.());
    }
    if (action === "cancel-book-clone") {
      const sourceId = state.bookCloneSourceId;
      resetBookClone();
      render("books", false);
      window.requestAnimationFrame?.(() => [...document.querySelectorAll('[data-action="open-book-clone"]')].find((button) => button.dataset.bookId === sourceId)?.focus?.());
    }
    if (action === "submit-book-clone") {
      const source = books().find((item) => bookId(item) === state.bookCloneSourceId);
      const destinationBookName = source ? bookCloneDestinationName(valueFor(source, "name", bookId(source)), state.bookCloneLanguageCode) : "";
      const destinationExists = Boolean(destinationBookName) && books().some((item) => String(valueFor(item, "name", bookId(item))).toLocaleLowerCase() === destinationBookName.toLocaleLowerCase());
      const writerActive = processIsActive() || productionActionActive() || state.cacheCleanupActive || applicationIsLoading();
      if (!source || !destinationBookName || destinationExists || state.bookClonePending || state.bookCloneAwaitingSnapshot || writerActive) return;
      state.bookClonePending = true;
      state.bookCloneDestination = destinationBookName;
      state.bookCloneFeedback = "Cloning Book…";
      state.bookCloneFeedbackError = false;
      send("book.clone", { bookId: state.bookCloneSourceId, languageCode: state.bookCloneLanguageCode });
      render("books", false);
    }
    if (action === "validate-brand") { const requestId = send("brand.validate", { brandName: state.inspectedBrand }); state.brandValidationRequestBrands.set(requestId, state.inspectedBrand); }
    if (action === "save-brand-author") {
      const brandName = target.dataset.brandName;
      beginCatalogMutation("brand.author.save", brandName, { brandName, author: state.brandAuthorDrafts.get(brandName) ?? "" });
    }
    if (action === "save-book-metadata") {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const summary = book ? summaryFor(book) : null;
      const draft = book && summary ? metadataDraftFor(book, summary, true) : null;
      if (!draft) { state.catalogFeedback = "Book not found."; state.catalogFeedbackError = true; updateBookCatalogMutationUi(); return; }
      const errors = validateBookMetadataDraft(draft);
      state.bookMetadataValidation.set(target.dataset.bookId, { attempted: true, errors });
      state.catalogMutationCommand = "book.metadata.save";
      state.catalogMutationTarget = target.dataset.bookId;
      if (errors.length) {
        state.catalogFeedback = metadataValidationSummary(errors);
        state.catalogFeedbackError = true;
        patchBookMetadataValidationUi(book, summary, true);
        return;
      }
      beginCatalogMutation("book.metadata.save", target.dataset.bookId, { bookId: target.dataset.bookId, ...draft });
    }
    if (action === "shuffle-book-keywords") {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const summary = book ? summaryFor(book) : null;
      const draft = book && summary ? keywordBuilderDraftFor(book, summary, true) : null;
      if (!draft) {
        state.catalogMutationCommand = "book.keywords.shuffle";
        state.catalogMutationTarget = target.dataset.bookId;
        state.catalogFeedback = "Book not found.";
        state.catalogFeedbackError = true;
        refreshBookKeywordBuilderCard();
        return;
      }
      const normalized = normalizeKeywordBuilderDraft(draft);
      state.bookKeywordBuilderValidation.delete(target.dataset.bookId);
      const clientRevision = keywordRevisionFor(target.dataset.bookId);
      state.keywordBuilderPending.set(target.dataset.bookId, "shuffle");
      state.catalogMutationCommand = "book.keywords.shuffle";
      state.catalogMutationTarget = target.dataset.bookId;
      state.catalogFeedback = "Shuffling preview…";
      state.catalogFeedbackError = false;
      send("book.keywords.shuffle", { bookId: target.dataset.bookId, bookKeywords: normalized.keywords, adsAsin: normalized.adsAsin || null, clientRevision });
      refreshBookKeywordBuilderCard();
    }
    if (action === "save-book-keywords") {
      const id = target.dataset.bookId;
      const book = books().find((item) => bookId(item) === id);
      const summary = book ? summaryFor(book) : null;
      const previewState = keywordPreviewFor(id);
      const receipt = String(valueFor(previewState, "receipt", "") ?? "");
      if (!book || !summary || !receipt) return;
      state.keywordBuilderPending.set(id, "save");
      state.keywordBuilderSubmitted = { bookId: id, draft: { ...keywordBuilderDraftFor(book, summary, true) } };
      beginCatalogMutation("book.keywords.save", id, { bookId: id, receipt, clientRevision: keywordRevisionFor(id) });
    }
    if (action === "copy-book-keywords") {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const output = book ? keywordOutputFor(target.dataset.bookId, summaryFor(book)) : null;
      if (!output) return;
      const copied = () => {
        state.catalogMutationCommand = "book.keywords.save";
        state.catalogMutationTarget = target.dataset.bookId;
        state.catalogFeedback = "Saved keyword fields copied to clipboard.";
        state.catalogFeedbackError = false;
        refreshBookKeywordBuilderCard();
      };
      const failed = () => {
        state.catalogMutationCommand = "book.keywords.save";
        state.catalogMutationTarget = target.dataset.bookId;
        state.catalogFeedback = "Clipboard access failed. Select and copy the saved fields manually.";
        state.catalogFeedbackError = true;
        refreshBookKeywordBuilderCard();
      };
      if (navigator.clipboard?.writeText) navigator.clipboard.writeText(keywordBuilderCopyText(output)).then(copied, failed);
      else failed();
    }
    if (action === "open-amazon-browser") {
      state.amazonBrowserPending = true;
      setAsinFeedback(target.dataset.bookId, "Opening the app-owned Amazon browser…");
      patchAsinResearch(target.dataset.bookId);
      send("amazon.browser.open", { bookId: target.dataset.bookId });
    }
    if (action === "crawl-amazon-asins") {
      const id = target.dataset.bookId;
      const book = books().find((item) => bookId(item) === id);
      const summary = book ? summaryFor(book) : null;
      const researchDraft = book && summary ? asinResearchDraftFor(book, summary) : null;
      const previewState = keywordPreviewFor(id);
      const previewReceipt = String(valueFor(previewState, "receipt", "") ?? "");
      if (!researchDraft || !previewReceipt) {
        setAsinFeedback(id, "Shuffle keywords before starting the ASIN crawl.", true);
        patchAsinResearch(id);
        return;
      }
      researchDraft.autoApplyPending = true;
      researchDraft.baseReceipt = previewReceipt;
      researchDraft.targetRevision = keywordRevisionFor(id);
      researchDraft.appliedRevision = -1;
      setAsinFeedback(id, "Preparing Amazon browser and search session…");
      patchAsinResearch(id);
      send("book.keywords.asin-crawl.start", { bookId: id, previewReceipt });
    }
    if (action === "cancel-amazon-asins") {
      setAsinFeedback(target.dataset.bookId, "Cancelling crawl…");
      patchAsinResearch(target.dataset.bookId);
      send("book.keywords.asin-crawl.cancel", { bookId: target.dataset.bookId });
    }
    if (action === "retry-keyword-refresh") {
      const drawerBody = document.querySelector(".book-drawer-body");
      if (drawerBody) state.bookDrawerScrollTop = drawerBody.scrollTop;
      state.keywordBuilderRefreshPending = true;
      state.keywordBuilderRefreshNeeded = false;
      state.keywordBuilderRefreshBookId = target.dataset.bookId;
      state.catalogMutationCommand = "book.keywords.save";
      state.catalogMutationTarget = target.dataset.bookId;
      state.catalogFeedback = "Saved. Refreshing library…";
      state.catalogFeedbackError = false;
      beginApplicationRefresh();
    }
    if (action === "assign-book-brand") {
      const bookIdValue = target.dataset.bookId;
      const summary = summaryFor(books().find((item) => bookId(item) === bookIdValue));
      const select = document.querySelector('[data-action="book-brand-select"]');
      const brandName = select?.value ?? "";
      if (!brandName) { state.catalogFeedback = "Select a matching Brand first."; state.catalogFeedbackError = true; refreshBookDrawerBody(); return; }
      const current = assignedBrandName(summary);
      if (current && current !== brandName && !window.confirm(`Reassign this Book from '${current}' to '${brandName}'? Existing files and outputs will not be moved or changed.`)) return;
      beginCatalogMutation("book.brand.assign", bookIdValue, { bookId: bookIdValue, brandName });
    }
    if (action === "unassign-book-brand") {
      const bookIdValue = target.dataset.bookId;
      if (!window.confirm("Unassign this Book from its Brand? Existing files and outputs will be kept.")) return;
      beginCatalogMutation("book.brand.unassign", bookIdValue, { bookId: bookIdValue });
    }
    if (action === "set-book-completion") {
      beginCatalogMutation("book.completion.set", target.dataset.bookId, {
        bookId: target.dataset.bookId,
        isCompleted: target.dataset.isCompleted === "true"
      });
    }
    if (action === "copy-brand-templates" && !state.brandTemplateCopyPending) {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const readiness = book ? brandTemplateCopyReadiness(book, summaryFor(book)) : { ready: false, reason: "Choose a Book first." };
      if (!readiness.ready) { status.textContent = readiness.reason; return; }
      state.brandTemplateCopyPending = true;
      refreshBookDrawerBody();
      send("book.brand.templates.copy", { bookId: target.dataset.bookId });
    }
    if (action === "upload-production-asset" && !state.productionImportPending && !productionActionActive()) {
      state.productionImportPending = target.dataset.productionAsset;
      state.productionFocusSelector = `[data-action="upload-production-asset"][data-production-asset="${target.dataset.productionAsset}"]`;
      state.productionFeedback = "Choose a PNG file in the system dialog.";
      state.productionFeedbackError = false;
      state.productionFeedbackWarning = false;
      updateProductionInteractionUi();
      send("book.production.asset.import", { bookId: target.dataset.bookId, assetKind: target.dataset.productionAsset });
    }
    if (action === "start-production-action" && !productionActionActive()) {
      state.productionActionName = target.dataset.productionAction;
      state.productionFocusSelector = `[data-action="start-production-action"][data-production-action="${target.dataset.productionAction}"]`;
      state.productionFeedback = "Queuing Production action…";
      state.productionFeedbackError = false;
      state.productionFeedbackWarning = false;
      updateProductionInteractionUi();
      send("book.production.action.start", { bookId: target.dataset.bookId, action: target.dataset.productionAction });
    }
    if (action === "randomize-pdf-names") {
      requestProductionPdfNames(target.dataset.bookId, true);
      refreshProductionWorkspace();
    }
    if (action === "build-final-interior" && !state.processStartPending) {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      const readiness = book ? productionFinalReadiness(book, summaryFor(book)) : { ready: false, reason: "Choose a Book first." };
      if (!readiness.ready) { state.productionFeedback = readiness.reason; state.productionFeedbackError = true; state.productionFeedbackWarning = false; updateProductionInteractionUi(); return; }
      state.processStartPending = true;
      state.productionFinalBuildActive = true;
      state.productionFocusSelector = '[data-action="build-final-interior"]';
      state.productionFeedback = "Starting Final Interior build…";
      state.productionFeedbackError = false;
      state.productionFeedbackWarning = false;
      updateProductionInteractionUi();
      send("process.start", { bookIds: [target.dataset.bookId], mode: "production-interior" });
    }
    if (action === "select-book" || action === "open-book-detail") openBookDetail(target.dataset.bookId, action === "open-book-detail" ? target : null);
    if (action === "save-book-interior-settings" && !state.bookInteriorSavePending) {
      const payload = interiorSavePayload(target.dataset.bookId);
      if (payload) {
        state.bookInteriorSavePending = true;
        updateInteriorSaveUi();
        send("book.interior.settings.save", payload);
      }
    }
    if (action === "random-interior" && !state.interiorShufflePending) {
      const targetBookId = target.dataset.bookId;
      if (hasInteriorDraft(targetBookId)) {
        state.interiorShuffleFeedback = "Save Interior changes first";
        state.interiorShuffleFeedbackError = true;
        refreshInteriorArtworkWorkspace();
        return;
      }
      state.interiorShufflePending = true;
      state.interiorShuffleFeedback = "Creating a new random Interior order…";
      state.interiorShuffleFeedbackError = false;
      refreshInteriorArtworkWorkspace();
      send("book.interior.shuffle", { bookId: targetBookId });
    }
    if (action === "open-interior-folder" && !state.interiorFolderOpenPending) {
      state.interiorFolderOpenPending = true;
      state.interiorFolderOpenBookId = target.dataset.bookId;
      refreshInteriorArtworkWorkspace();
      send("book.interior.open-folder", { bookId: target.dataset.bookId });
    }
    if (action === "intro-add-template" || action === "intro-remove-template" || action === "intro-move-template") {
      const book = books().find((item) => bookId(item) === target.dataset.bookId);
      if (book) {
        const summary = summaryFor(book);
        const current = effectiveIntro(book, summary);
        let sourceReferences = [...current.sourceReferences];
        if (action === "intro-add-template") sourceReferences.push(target.dataset.introSourceReference);
        if (action === "intro-remove-template") sourceReferences = sourceReferences.filter((reference) => reference.toLowerCase() !== target.dataset.introSourceReference.toLowerCase());
        if (action === "intro-move-template") {
          const index = Number(target.dataset.introIndex);
          const next = target.dataset.introDirection === "up" ? index - 1 : index + 1;
          if (Number.isInteger(index) && next >= 0 && next < sourceReferences.length) [sourceReferences[index], sourceReferences[next]] = [sourceReferences[next], sourceReferences[index]];
        }
        stageIntroChange(book, summary, true, sourceReferences);
        status.textContent = "Unsaved Intro and Interior changes";
        refreshIntroTemplateWorkspace();
      }
    }
    if (action === "intro-template-page") {
      const book = selectedBook();
      const summary = book ? summaryFor(book) : null;
      const selection = book && summary ? effectiveIntro(book, summary) : null;
      const itemCount = selection?.hasIntro ? assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior").length : (valueFor(assignedBrandFor(summary), "introTemplateAssets", []) ?? []).filter((asset) => /\.(png|jpe?g)$/i.test(valueFor(asset, "fileName", ""))).length;
      const last = Math.max(1, Math.ceil(itemCount / 6));
      state.introTemplatePage = Math.min(last, Math.max(1, state.introTemplatePage + (target.dataset.introTemplatePage === "next" ? 1 : -1)));
      refreshIntroTemplateWorkspace(target.dataset.introTemplatePage);
    }
    if (action === "queue-book") { const id = target.dataset.bookId; if (target.checked) state.selectedBookIds.add(id); else state.selectedBookIds.delete(id); }
    if (action === "queue-selected-book") {
      const book = books().find((item) => bookId(item) === state.selectedBookId);
      const readiness = book ? processingReadiness(book, summaryFor(book)) : { ready: false, reason: "Choose a Book first." };
      if (!readiness.ready) { status.textContent = readiness.reason; return; }
      state.selectedBookIds.add(state.selectedBookId);
      state.processQueuePage = 1;
      state.processQueueScrollTop = 0;
      render("process");
    }
    if (action === "toggle-artwork-selection") {
      const reference = target.dataset.sourceReference;
      if (state.selectedArtworkReferences.has(reference)) state.selectedArtworkReferences.delete(reference); else state.selectedArtworkReferences.add(reference);
      refreshInteriorArtworkWorkspace();
    }
    if (action === "toggle-all-artwork") {
      const book = selectedBook();
      const summary = book ? summaryFor(book) : null;
      if (book && summary) {
        const intro = effectiveIntro(book, summary);
        const shown = assetsFor(summary).filter((asset) => valueFor(asset, "kind", "") === "Interior" && (!state.assetStatus || (state.assetStatus === "Active" ? effectiveInteriorAsset(book, asset).isActive : !effectiveInteriorAsset(book, asset).isActive)) && (!state.assetFrameMode || effectiveInteriorAsset(book, asset).frameMode === state.assetFrameMode) && !intro.sourceReferences.some((reference) => reference.toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase()));
        shown.forEach((asset) => { const reference = String(valueFor(asset, "sourceReference", "")); if (target.checked) state.selectedArtworkReferences.add(reference); else state.selectedArtworkReferences.delete(reference); });
        refreshInteriorArtworkWorkspace();
      }
    }
    if (action === "apply-artwork-bulk") {
      const book = selectedBook();
      const summary = book ? summaryFor(book) : null;
      if (book && summary && state.selectedArtworkReferences.size) {
        const intro = effectiveIntro(book, summary);
        const selectedAssets = assetsFor(summary).filter((asset) => state.selectedArtworkReferences.has(String(valueFor(asset, "sourceReference", ""))) && !intro.sourceReferences.some((reference) => reference.toLowerCase() === String(valueFor(asset, "sourceReference", "")).toLowerCase()));
        selectedAssets.forEach((asset) => {
          if (state.assetBulkActive !== "unchanged") stageInteriorAssetChange(book, asset, "active", state.assetBulkActive === "active");
          if (state.assetBulkFrameMode !== "unchanged") stageInteriorAssetChange(book, asset, "frameMode", state.assetBulkFrameMode);
        });
        status.textContent = `Applied Interior changes to ${selectedAssets.length} artwork`;
        refreshInteriorArtworkWorkspace();
      }
    }
    if (action === "book-tab") {
      state.selectedBookTab = ["settings", "asin", "production", "artwork", "pages"].includes(target.dataset.bookTab) ? target.dataset.bookTab : "settings";
      if (state.selectedBookTab === "production") requestProductionPdfNames(state.selectedBookId);
      refreshBookDrawerBody(state.selectedBookTab);
      if (state.selectedBookTab === "asin") {
        const book = selectedBook();
        const saved = book ? keywordBuilderFor(summaryFor(book)) : null;
        const buildId = String(valueFor(saved, "buildId", "") ?? "");
        if (buildId && Number(valueFor(saved, "algorithmVersion", 0)) === 4 && !keywordPreviewFor(state.selectedBookId)) {
          const clientRevision = keywordRevisionFor(state.selectedBookId);
          state.keywordBuilderPending.set(state.selectedBookId, "open");
          state.catalogMutationCommand = "book.keywords.preview.open";
          state.catalogMutationTarget = state.selectedBookId;
          send("book.keywords.preview.open", { bookId: state.selectedBookId, buildId, clientRevision });
        }
      }
    }
    if (action === "select-asset") { state.selectedAssetReference = target.dataset.sourceReference; render("books", false); }
    if (action === "asset-view") { state.assetView = target.dataset.assetView; render("books", false); }
    if (action === "asset-status") { const status = ["Active", "Inactive"].includes(target.dataset.assetStatus) ? target.dataset.assetStatus : ""; state.assetStatus = state.assetStatus === status ? "" : status; state.artworkGridScrollTop = 0; refreshInteriorArtworkWorkspace(); }
    if (action === "asset-frame-mode") { const mode = ["", "enabled", "disabled"].includes(target.dataset.assetFrameMode) ? target.dataset.assetFrameMode : ""; state.assetFrameMode = mode; state.artworkGridScrollTop = 0; refreshInteriorArtworkWorkspace(); }
    if (action === "clear-artwork-filters") { state.assetStatus = "Active"; state.assetFrameMode = ""; state.artworkGridScrollTop = 0; refreshInteriorArtworkWorkspace(); }
    if (action === "clear-cache" && !cacheCleanupBlocked()) {
      if (window.confirm("Clear processed image cache for completed Books?")) {
        state.cacheCleanupResultRequested = false;
        send("cache.clear");
      }
    }
    if (action === "book-page") { const last = Number(target.closest("[data-book-total-pages]")?.dataset.bookTotalPages ?? 1); state.bookPage = target.dataset.bookPage === "first" ? 1 : target.dataset.bookPage === "last" ? last : Math.min(last, Math.max(1, state.bookPage + (target.dataset.bookPage === "next" ? 1 : -1))); render("books", false); }
    if (action === "pdf-library-page") { const totalPages = Math.max(1, Math.ceil(pdfLibraryBooks().length / pdfLibraryPageSize)); state.pdfLibraryPage = target.dataset.pdfLibraryPage === "first" ? 1 : target.dataset.pdfLibraryPage === "last" ? totalPages : Math.min(totalPages, Math.max(1, state.pdfLibraryPage + (target.dataset.pdfLibraryPage === "next" ? 1 : -1))); render("outputs", false); }
    if (action === "pdf-library-view") { state.pdfLibraryView = target.dataset.pdfLibraryView === "list" ? "list" : "grid"; render("outputs", false); }
    if (action === "process-queue-page") { const last = Number(target.closest("[data-process-queue-total-pages]")?.dataset.processQueueTotalPages ?? 1); state.processQueuePage = target.dataset.processQueuePage === "first" ? 1 : target.dataset.processQueuePage === "last" ? last : Math.min(last, Math.max(1, state.processQueuePage + (target.dataset.processQueuePage === "next" ? 1 : -1))); state.processQueueScrollTop = 0; render("process", false); }
    if (action === "remove-process-queue-book" && !processIsActive()) {
      const id = target.dataset.bookId;
      const book = books().find((item) => bookId(item) === id);
      const name = valueFor(book, "name", id);
      if (!window.confirm(`Remove ${name} from the selected queue?`)) return;
      state.selectedBookIds.delete(id);
      state.processQueueScrollTop = 0;
      status.textContent = `${name} removed from selected queue`;
      render("process", false);
    }
    if (action === "validate-book") send("book.validate", { bookId: target.dataset.bookId });
    if (action === "go-process") render("process");
    if (action === "go-books") render("books", false);
    if (action === "start-process" && !state.processStartPending) {
      const readiness = selectedProcessingReadiness();
      if (!readiness.ready) { status.textContent = readiness.reason; return; }
      state.processStartPending = true;
      send("process.start", { bookIds: [...state.selectedBookIds], mode: "interior-only" });
    }
    if (action === "cancel-process") send("process.cancel");
    if (action === "preview-output") beginPdfLibraryAction("book.output.preview", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference }, `preview:${target.dataset.bookId}:${target.dataset.artifactReference}`, target);
    if (action === "open-output-folder") beginPdfLibraryAction("book.output.open-folder", { bookId: target.dataset.bookId }, `folder:${target.dataset.bookId}`, target);
    if (action === "open-output") send("book.output.open", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
    if (action === "reveal-output") send("book.output.reveal", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
    if (action === "copy-output-path") send("book.output.copy-path", { bookId: target.dataset.bookId, artifactReference: target.dataset.artifactReference });
  });
  content.addEventListener("submit", (event) => {
    if (event.target.dataset.form !== "configuration") return;
    event.preventDefault();
    beginSettingsSave();
  });
  content.addEventListener("input", (event) => {
    if (Object.hasOwn(event.target.dataset, "genericKeywords") && state.settingsGenericLanguageCode) {
      state.settingsGenericKeywordDrafts.set(state.settingsGenericLanguageCode, String(event.target.value ?? ""));
    }
    if (event.target.dataset.amazonProfileField && state.settingsAmazonLanguageCode) {
      const draft = { ...(state.settingsAmazonProfileDrafts.get(state.settingsAmazonLanguageCode) ?? {}) };
      draft[event.target.dataset.amazonProfileField] = String(event.target.value ?? "");
      state.settingsAmazonProfileDrafts.set(state.settingsAmazonLanguageCode, draft);
    }
    if (!state.settingsSavePending && (event.target.dataset.setting || Object.hasOwn(event.target.dataset, "genericKeywords") || event.target.dataset.amazonProfileField)) {
      state.settingsFeedback = "";
      state.settingsFeedbackError = false;
      updateSettingsSaveUi();
    }
    if (event.target.dataset.action === "filter-books") { state.bookFilter = event.target.value; state.bookPage = 1; render("books", false); }
    if (event.target.dataset.action === "filter-brands") { state.brandFilter = event.target.value; refreshBrandList(); }
    if (event.target.dataset.action === "pdf-library-search") { state.pdfLibrarySearch = event.target.value; state.pdfLibraryPage = 1; state.pdfLibrarySearchFocused = true; state.pdfLibrarySearchCaret = event.target.selectionStart ?? event.target.value.length; render("outputs", false); }
    if (event.target.dataset.action === "book-metadata-input") {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      const summary = book ? summaryFor(book) : null;
      if (book && summary) {
        const draft = metadataDraftFor(book, summary, true);
        const field = event.target.dataset.metadataField;
        const selectionStart = event.target.selectionStart;
        const selectionEnd = event.target.selectionEnd;
        const nextValue = field === "title" ? String(event.target.value ?? "").toUpperCase() : event.target.value;
        if (event.target.value !== nextValue) {
          event.target.value = nextValue;
          if (Number.isInteger(selectionStart) && Number.isInteger(selectionEnd)) event.target.setSelectionRange?.(selectionStart, selectionEnd);
        }
        draft[field] = nextValue;
        const dirty = hasMetadataDraft(book, summary);
        const validation = metadataValidationFor(bookId(book));
        if (!dirty) {
          state.bookMetadataValidation.delete(bookId(book));
          if (state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.metadata.save") {
            state.catalogFeedback = "";
            state.catalogFeedbackError = false;
          }
        } else if (validation.attempted && metadataFieldOrder.includes(field)) {
          const errors = [
            ...validation.errors.filter((error) => error.field !== field),
            ...validateBookMetadataDraft(draft, field)
          ].sort((left, right) => metadataFieldOrder.indexOf(left.field) - metadataFieldOrder.indexOf(right.field));
          state.bookMetadataValidation.set(bookId(book), { attempted: true, errors });
          state.catalogMutationCommand = "book.metadata.save";
          state.catalogMutationTarget = bookId(book);
          state.catalogFeedback = errors.length ? metadataValidationSummary(errors) : "";
          state.catalogFeedbackError = errors.length > 0;
        }
        patchBookMetadataValidationUi(book, summary);
        const assignmentWarning = metadataAssignmentWarning(draft, summary);
        const assignmentWarningElement = document.getElementById("book-author-assignment-warning");
        if (assignmentWarningElement) { assignmentWarningElement.hidden = !assignmentWarning; assignmentWarningElement.textContent = assignmentWarning; }
        const unsaved = document.querySelector("[data-book-interior-unsaved]");
        if (unsaved) unsaved.hidden = !(hasInteriorDraft(bookId(book)) || hasMetadataDraft(book, summary) || hasKeywordBuilderDraft(book, summary));
      }
    }
    if (["book-keyword-source", "book-keyword-ads-asin"].includes(event.target.dataset.action)) {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      const summary = book ? summaryFor(book) : null;
      if (book && summary) {
        const draft = keywordBuilderDraftFor(book, summary, true);
        if (event.target.dataset.action === "book-keyword-source") draft.sourceText = event.target.value;
        else draft.adsAsin = event.target.value;
        invalidateKeywordPreview(bookId(book));
        state.bookKeywordBuilderValidation.delete(bookId(book));
        if (state.catalogMutationTarget === bookId(book) && state.catalogMutationCommand === "book.keywords.save") {
          state.catalogFeedback = "";
          state.catalogFeedbackError = false;
        }
        refreshBookKeywordBuilderCard();
        const unsaved = document.querySelector("[data-book-interior-unsaved]");
        if (unsaved) unsaved.hidden = !(hasInteriorDraft(bookId(book)) || hasMetadataDraft(book, summary) || hasKeywordBuilderDraft(book, summary));
      }
    }
    if (event.target.dataset.action === "brand-author-input") {
      state.brandAuthorDrafts.set(event.target.dataset.brandName, event.target.value);
      const selected = brands().find((brand) => valueFor(brand, "name", "") === event.target.dataset.brandName);
      const save = document.querySelector('[data-action="save-brand-author"]');
      if (save) save.disabled = !selected || !brandAuthorIsDirty(selected, event.target.value) || state.catalogMutationPending || processIsActive();
    }
  });
  content.addEventListener("change", (event) => {
    if (event.target.dataset.action === "generic-keyword-language") showGenericKeywordLanguage(event.target.value);
    if (event.target.dataset.action === "amazon-market-language") showAmazonMarketplaceLanguage(event.target.value);
    if (event.target.dataset.action === "clone-brand-language") { state.brandCloneLanguageCode = event.target.value; state.brandCloneFeedback = ""; state.brandCloneFeedbackError = false; render("brands", false); }
    if (event.target.dataset.action === "clone-book-language") { state.bookCloneLanguageCode = event.target.value; state.bookCloneFeedback = ""; state.bookCloneFeedbackError = false; render("books", false); window.requestAnimationFrame?.(() => document.querySelector('[data-action="clone-book-language"]')?.focus?.()); }
    if (event.target.dataset.action === "book-status") { state.bookStatus = bookStatuses.includes(event.target.value) ? event.target.value : "All"; state.bookPage = 1; render("books", false); }
    if (event.target.dataset.action === "diagnostic-book") { state.selectedBookId = event.target.value; render("diagnostics", false); }
    if (event.target.dataset.action === "set-book-background") {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      if (book) { stageBackgroundChange(book, summaryFor(book), event.target.checked); status.textContent = "Unsaved Interior changes"; updateInteriorSaveUi(); }
    }
    if (event.target.dataset.action === "set-intro-mode") {
      const book = books().find((item) => bookId(item) === event.target.dataset.bookId);
      if (book) {
        const summary = summaryFor(book);
        const current = effectiveIntro(book, summary);
        stageIntroChange(book, summary, event.target.value === "custom", current.sourceReferences);
        state.introTemplatePage = 1;
        status.textContent = "Unsaved Intro and Interior changes";
        refreshIntroTemplateWorkspace();
      }
    }
    if (event.target.dataset.action === "set-artwork-bulk-active") { state.assetBulkActive = ["active", "inactive"].includes(event.target.value) ? event.target.value : "unchanged"; refreshInteriorArtworkWorkspace(); }
    if (event.target.dataset.action === "set-artwork-bulk-frame-mode") { state.assetBulkFrameMode = ["enabled", "disabled"].includes(event.target.value) ? event.target.value : "unchanged"; refreshInteriorArtworkWorkspace(); }
    if (event.target.dataset.action === "book-sort") { state.bookSort = event.target.value; state.bookPage = 1; render("books", false); }
    if (event.target.dataset.action === "book-brand-filter") { state.bookBrandFilter = event.target.value; state.bookPage = 1; state.selectedBookIds.clear(); render("books", false); }
    if (event.target.dataset.action === "book-brand-select") { const assign = document.querySelector('[data-action="assign-book-brand"]'); const book = selectedBook(); const summary = book ? summaryFor(book) : null; if (assign) assign.disabled = !event.target.value || event.target.value === assignedBrandName(summary) || catalogMutationBusy() || processIsActive(); }
    if (event.target.dataset.action === "brand-author-input") render("brands", false);
    if (event.target.dataset.action === "pdf-library-sort") { state.pdfLibrarySort = ["newest", "name", "size"].includes(event.target.value) ? event.target.value : "newest"; state.pdfLibraryPage = 1; render("outputs", false); }
  });
  content.addEventListener("error", (event) => {
    const image = event.target;
    if (!image?.matches?.("img[data-local-image]")) return;
    if (image.dataset.introTemplateId) {
      state.introTemplateDimensions.set(image.dataset.introTemplateId, { valid: false });
    }
    const fallback = document.createElement("span");
    fallback.className = "book-preview-fallback";
    fallback.setAttribute("aria-label", image.dataset.imageFallback || "Image unavailable");
    fallback.textContent = image.dataset.imageFallback || "Image unavailable";
    image.replaceWith(fallback);
    if (image.dataset.introTemplateId && state.bookDrawerOpen && state.selectedBookTab === "settings") render("books", false);
  }, true);
  content.addEventListener("load", (event) => {
    const image = event.target;
    if (!image?.matches?.("img[data-local-image]") || !image.dataset.introTemplateId) return;
    const valid = isSupportedIntroTemplateSize(image.naturalWidth, image.naturalHeight);
    const previous = state.introTemplateDimensions.get(image.dataset.introTemplateId);
    if (previous?.valid === valid && previous.width === image.naturalWidth && previous.height === image.naturalHeight) return;
    state.introTemplateDimensions.set(image.dataset.introTemplateId, { valid, width: image.naturalWidth, height: image.naturalHeight });
    if (state.bookDrawerOpen && state.selectedBookTab === "settings") render("books", false);
  }, true);
  window.chrome.webview.addEventListener("message", (event) => {
    const response = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
    const responseId = valueFor(response, "id", "");
    const requestCommand = state.pendingCommands.get(responseId) ?? "";
    const storageRequestBookId = state.storageRequestBooks.get(responseId) ?? "";
    const validationRequestBrand = state.brandValidationRequestBrands.get(responseId) ?? "";
    const productionPdfNameRequest = state.productionPdfNameRequests.get(responseId) ?? null;
    const pdfLibraryAction = finishPdfLibraryAction(responseId);
    state.pendingCommands.delete(responseId);
    state.storageRequestBooks.delete(responseId);
    state.brandValidationRequestBrands.delete(responseId);
    state.productionPdfNameRequests.delete(responseId);
    const ok = valueFor(response, "ok", false);
    const command = valueFor(response, "command", "");
    if (ok && command === "updates.state") {
      applyUpdateSnapshot(valueFor(response, "payload", {}));
    } else if (ok && command === "app.pong") {
      status.textContent = "Connected";
    } else if (ok && command === "s3.snapshot") {
      state.storageSnapshot = valueFor(response, "payload", {});
      state.storageLoading = false;
      state.storageSettingsPending = false;
      state.storageFeedbackError = false;
      for (const book of valueFor(state.storageSnapshot, "books", [])) {
        const session = valueFor(book, "session", null);
        if (session && valueFor(session, "isActive", false)) observeStorageSession(session);
      }
      updateGlobalProcessStatus();
      if (currentRoute() === "configuration") renderConfiguration();
      if (state.selectedBookId) patchBookS3(state.selectedBookId);
      status.textContent = "S3 status refreshed";
    } else if (ok && command === "s3.credentials.status") {
      state.storageSettingsPending = false;
      state.storageFeedback = "Credentials replaced";
      state.storageFeedbackError = false;
      state.storageLoading = false;
      loadStorage();
      if (currentRoute() === "configuration") renderConfiguration();
      status.textContent = "S3 credentials replaced";
    } else if (ok && command === "book.s3.session") {
      const session = valueFor(response, "payload", {});
      observeStorageSession(session);
      updateGlobalProcessStatus();
      const view = valueFor(session, "view", {});
      const active = valueFor(session, "isActive", false);
      const outcome = storageOutcomeName(valueFor(view, "outcome", "Pending"));
      status.textContent = active ? `${storageActionName(valueFor(view, "action", "Check"))} in progress` : outcome === "CompletedWithErrors" ? "Storage completed with errors" : "Storage action completed";
    } else if (ok && command === "amazon.browser.status") {
      state.amazonBrowserPending = false;
      state.amazonBrowserStatus = valueFor(response, "payload", { state: "Closed" });
      if (state.selectedBookId) {
        const browserState = browserStateName(valueFor(state.amazonBrowserStatus, "state", "Closed"));
        setAsinFeedback(state.selectedBookId, browserState === "Ready" ? "Amazon browser is ready." : browserState === "NeedsAttention" ? "Amazon needs attention in the browser." : "");
        patchAsinResearch(state.selectedBookId);
      }
      status.textContent = browserStateName(valueFor(state.amazonBrowserStatus, "state", "Closed")) === "Ready" ? "Amazon browser ready" : "Connected";
    } else if (ok && command === "book.keywords.preview") {
      const payload = valueFor(response, "payload", {});
      const previewBookId = String(valueFor(payload, "bookId", "") ?? "");
      const clientRevision = Number(valueFor(payload, "clientRevision", -1));
      if (previewBookId && clientRevision === keywordRevisionFor(previewBookId)) {
        state.keywordBuilderPreviews.set(previewBookId, payload);
        state.bookKeywordBuilderValidation.delete(previewBookId);
        state.catalogMutationCommand = requestCommand;
        state.catalogMutationTarget = previewBookId;
        state.catalogFeedback = requestCommand === "book.keywords.preview.update-ads-asin" ? "Crawled ASINs added to the preview. Save when ready." : "Preview shuffled. Review it, then Save.";
        state.catalogFeedbackError = false;
        if (requestCommand === "book.keywords.preview.update-ads-asin") {
          setAsinFeedback(previewBookId, "Crawled ASINs added to the preview. Save when ready.");
        }
      }
      state.keywordBuilderPending.delete(previewBookId || state.selectedBookId);
      if (currentRoute() === "books" && state.bookDrawerOpen) refreshBookKeywordBuilderCard();
      status.textContent = "Keyword preview ready";
    } else if (ok && command === "book.keywords.asin-crawl") {
      const session = valueFor(response, "payload", {});
      const sessionBookId = String(valueFor(session, "bookId", "") ?? "");
      if (sessionBookId && sessionBookId !== state.selectedBookId && valueFor(session, "isActive", false)) {
        setAsinFeedback(state.selectedBookId, `Another ASIN crawl is active for ${sessionBookId}.`, true);
        patchAsinResearch(state.selectedBookId);
      }
      const autoDraft = autoStageAsinCrawlResults(session);
      observeAmazonAsinCrawl(session);
      const view = asinSessionView(session);
      const outcome = asinOutcomeName(valueFor(view, "outcome", "Idle"));
      if (sessionBookId && !asinSessionActive(session) && outcome !== "Idle") {
        const count = String(valueFor(view, "finalAsins", "") ?? "").split(",").filter(Boolean).length;
        const resultMessage = autoDraft.status === "updating"
          ? `${count} ASIN${count === 1 ? "" : "s"} found. Updating the generated preview…`
          : autoDraft.status === "unchanged" ? `${count} ASIN${count === 1 ? " is" : "s are"} already in the Ads ASIN draft.`
            : autoDraft.status === "stale" ? "Keyword Builder inputs changed during the crawl. Shuffle and crawl again."
              : autoDraft.status === "invalid" ? "Crawl returned an invalid ASIN result. Crawl again."
                : count ? `${count} ASIN${count === 1 ? "" : "s"} available in Crawl Results.` : "No matching ASINs found. Adjust Search Keywords and crawl again.";
        const outcomePrefix = outcome === "Completed" ? ""
          : outcome === "NeedsAttention" ? "Amazon needs attention. "
            : outcome === "Cancelled" ? "Crawl cancelled. "
              : outcome === "Failed" ? "Crawl stopped. " : "Crawl finished with partial results. ";
        setAsinFeedback(sessionBookId, `${outcomePrefix}${resultMessage}`, outcome === "Failed" || outcome === "NeedsAttention" || autoDraft.status === "invalid");
        patchAsinResearch(sessionBookId);
      }
    } else if (ok && command === "book.keywords.saved") {
      const payload = valueFor(response, "payload", {});
      const savedBookId = String(valueFor(payload, "bookId", state.catalogMutationTarget));
      const savedBuilder = valueFor(payload, "keywordBuilder", null);
      const refreshTask = valueFor(payload, "refreshTask", null);
      const refreshWarning = String(valueFor(payload, "refreshWarning", "") ?? "");
      state.catalogMutationPending = false;
      state.catalogMutationAwaitingSnapshot = false;
      state.keywordBuilderPending.delete(savedBookId);
      if (savedBuilder) {
        setSummaryKeywordBuilder(savedBookId, savedBuilder);
        state.keywordBuilderConfirmed.set(savedBookId, savedBuilder);
      }
      const submitted = state.keywordBuilderSubmitted;
      const currentDraft = state.bookKeywordBuilderDrafts.get(savedBookId);
      if (submitted?.bookId === savedBookId && currentDraft && keywordBuilderDraftMatches(currentDraft, submitted.draft)) {
        state.bookKeywordBuilderDrafts.delete(savedBookId);
      }
      state.keywordBuilderSubmitted = null;
      state.keywordBuilderPreviews.delete(savedBookId);
      state.bookKeywordBuilderValidation.delete(savedBookId);
      state.keywordBuilderRefreshBookId = savedBookId;
      state.keywordBuilderRefreshPending = Boolean(refreshTask) && !refreshWarning;
      state.keywordBuilderRefreshNeeded = !state.keywordBuilderRefreshPending;
      state.catalogFeedback = state.keywordBuilderRefreshPending ? "Saved. Refreshing library…" : "Saved. Library refresh needs attention.";
      state.catalogFeedbackError = false;
      if (currentRoute() === "books" && state.bookDrawerOpen) refreshBookKeywordBuilderCard();
      const book = selectedBook();
      const summary = book ? summaryFor(book) : null;
      const unsaved = document.querySelector("[data-book-interior-unsaved]");
      if (unsaved && book && summary) unsaved.hidden = !(hasInteriorDraft(bookId(book)) || hasMetadataDraft(book, summary) || hasKeywordBuilderDraft(book, summary));
      status.textContent = "Keyword Builder saved";
      if (refreshTask) observeLibraryRefresh(refreshTask);
    } else if (ok && command === "brand.clone.completed") {
      const payload = valueFor(response, "payload", {});
      const refreshTask = valueFor(payload, "refreshTask", null);
      const refreshWarning = String(valueFor(payload, "refreshWarning", "") ?? "");
      state.brandClonePending = false;
      state.brandCloneAwaitingSnapshot = true;
      state.brandCloneDestination = String(valueFor(payload, "destinationBrandName", state.brandCloneDestination));
      state.brandCloneFeedback = refreshTask && !refreshWarning ? "Brand cloned. Refreshing library…" : "Brand was cloned, but the library refresh could not start. Use Refresh to continue.";
      state.brandCloneFeedbackError = !refreshTask || Boolean(refreshWarning);
      if (currentRoute() === "brands") render("brands", false);
      status.textContent = "Brand cloned";
      if (refreshTask && !refreshWarning) observeLibraryRefresh(refreshTask);
    } else if (ok && command === "book.clone.completed") {
      const payload = valueFor(response, "payload", {});
      const refreshTask = valueFor(payload, "refreshTask", null);
      const refreshWarning = String(valueFor(payload, "refreshWarning", "") ?? "");
      state.bookClonePending = false;
      state.bookCloneAwaitingSnapshot = true;
      state.bookCloneDestinationId = String(valueFor(payload, "destinationBookId", state.bookCloneDestinationId));
      state.bookCloneDestination = String(valueFor(payload, "destinationBookName", state.bookCloneDestination));
      state.bookCloneFeedback = refreshTask && !refreshWarning ? "Book cloned. Refreshing library…" : "Book was cloned, but the library refresh could not start. Use Refresh to continue.";
      state.bookCloneFeedbackError = !refreshTask || Boolean(refreshWarning);
      if (currentRoute() === "books") render("books", false);
      status.textContent = "Book cloned";
      if (refreshTask && !refreshWarning) observeLibraryRefresh(refreshTask);
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "LibraryRefresh") {
      if (requestCommand === "book.interior.settings.save") {
        state.bookInteriorSaveTaskId = valueFor(valueFor(response, "payload", {}), "taskId", "");
        state.bookInteriorSavePending = false;
        clearInteriorDraft(state.selectedBookId);
        clearArtworkBulkSelection();
        status.textContent = "Interior changes saved";
        updateInteriorSaveUi();
      }
      if (requestCommand === "book.interior.shuffle") {
        state.interiorShuffleTaskId = valueFor(valueFor(response, "payload", {}), "taskId", "");
        state.interiorShufflePending = false;
        state.interiorShuffleFeedback = "Random order saved. Refreshing Interior status…";
        state.interiorShuffleFeedbackError = false;
        status.textContent = "Interior order randomized";
        if (state.bookDrawerOpen && state.selectedBookTab === "artwork") refreshInteriorArtworkWorkspace();
      }
      if (["book.completion.set", "book.metadata.save", "book.brand.assign", "book.brand.unassign", "brand.author.save"].includes(requestCommand)) {
        state.catalogMutationPending = false;
        state.catalogMutationAwaitingSnapshot = true;
        state.catalogFeedback = "Saved. Refreshing library…";
        state.catalogFeedbackError = false;
        if (currentRoute() === "books" && state.bookDrawerOpen) updateBookCatalogMutationUi();
        if (currentRoute() === "brands") render("brands", false);
      }
      observeLibraryRefresh(valueFor(response, "payload", {}));
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "CacheCleanup") {
      observeCacheCleanup(valueFor(response, "payload", {}));
    } else if (ok && command === "background.task" && valueFor(valueFor(response, "payload", {}), "kind", "") === "ProductionAction") {
      observeProductionAction(valueFor(response, "payload", {}));
    } else if (ok && command === "app.snapshot") {
      const cloneWasAwaiting = state.brandCloneAwaitingSnapshot;
      const cloneDestination = state.brandCloneDestination;
      const bookCloneWasAwaiting = state.bookCloneAwaitingSnapshot;
      const bookCloneDestinationId = state.bookCloneDestinationId;
      const bookCloneDestination = state.bookCloneDestination;
      const interiorSaveWasAwaiting = state.bookInteriorSaveAwaitingSnapshot;
      const interiorShuffleWasAwaiting = state.interiorShuffleAwaitingSnapshot;
      const preserveBookDrawer = (interiorSaveWasAwaiting || interiorShuffleWasAwaiting) && state.bookDrawerOpen && currentRoute() === "books";
      const preserveProductionDrawer = state.productionRefreshAwaitingSnapshot && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books";
      const preserveCatalogDrawer = state.catalogMutationAwaitingSnapshot && state.bookDrawerOpen && currentRoute() === "books" && state.catalogMutationCommand.startsWith("book.");
      const preserveKeywordDrawer = state.keywordBuilderRefreshPending && state.bookDrawerOpen && currentRoute() === "books";
      state.bookInteriorSaveAwaitingSnapshot = false;
      state.bookInteriorSaveTaskId = "";
      state.interiorShuffleAwaitingSnapshot = false;
      state.interiorShuffleTaskId = "";
      state.interiorShufflePending = false;
      state.interiorShuffleFeedback = "";
      state.interiorShuffleFeedbackError = false;
      state.productionRefreshAwaitingSnapshot = false;
      const catalogWasAwaiting = state.catalogMutationAwaitingSnapshot;
      const catalogTarget = state.catalogMutationTarget;
      const catalogCommand = state.catalogMutationCommand;
      const incomingSnapshot = valueFor(response, "payload", {});
      const incomingSummaries = valueFor(incomingSnapshot, "bookSummaries", []);
      for (const [confirmedBookId, confirmedBuilder] of state.keywordBuilderConfirmed) {
        const incomingSummary = incomingSummaries.find((item) => valueFor(valueFor(item, "bookId", {}), "value", "") === confirmedBookId);
        if (!incomingSummary) continue;
        const incomingBuilder = valueFor(incomingSummary, "keywordBuilder", null);
        if (valueFor(incomingBuilder, "buildId", "") === valueFor(confirmedBuilder, "buildId", "")) {
          state.keywordBuilderConfirmed.delete(confirmedBookId);
        } else if (Object.hasOwn(incomingSummary, "KeywordBuilder") && !Object.hasOwn(incomingSummary, "keywordBuilder")) {
          incomingSummary.KeywordBuilder = confirmedBuilder;
        } else {
          incomingSummary.keywordBuilder = confirmedBuilder;
        }
      }
      window.appSnapshot = incomingSnapshot;
      state.settingsGenericKeywordDraftsInitialized = false;
      state.settingsAmazonProfileDraftsInitialized = false;
      if (cloneWasAwaiting) {
        const clonedBrand = valueFor(discovery(), "brands", []).find((brand) => String(valueFor(brand, "name", "")) === cloneDestination);
        if (clonedBrand) {
          if (state.brandFilter && !cloneDestination.toLocaleLowerCase().includes(state.brandFilter.trim().toLocaleLowerCase())) state.brandFilter = "";
          state.inspectedBrand = cloneDestination;
          resetBrandClone(false);
          state.brandCloneNotice = "Brand cloned. Validate this Brand before processing.";
        } else {
          state.brandClonePending = false;
          state.brandCloneFeedback = "Brand was cloned, but it was not found in the refreshed library. Refresh and try again.";
          state.brandCloneFeedbackError = true;
        }
      }
      if (bookCloneWasAwaiting) {
        const clonedBook = books().find((book) => bookId(book) === bookCloneDestinationId)
          ?? books().find((book) => String(valueFor(book, "name", bookId(book))) === bookCloneDestination);
        if (clonedBook) {
          const clonedBookId = bookId(clonedBook);
          resetBookClone(false);
          state.bookCloneNotice = `Book '${bookCloneDestination}' was cloned successfully.`;
          state.bookCloneNoticeBookId = clonedBookId;
          state.selectedBookId = clonedBookId;
          state.bookDrawerOpen = true;
          const clonedBookIndex = filteredBooks().findIndex((book) => bookId(book) === clonedBookId);
          if (clonedBookIndex >= 0) state.bookPage = Math.floor(clonedBookIndex / 12) + 1;
        } else {
          state.bookClonePending = false;
          state.bookCloneFeedback = "Book was cloned, but it was not found in the refreshed library. Refresh and try again.";
          state.bookCloneFeedbackError = true;
        }
      }
      loadStorage();
      if (state.keywordBuilderRefreshNeeded && !state.keywordBuilderConfirmed.has(state.keywordBuilderRefreshBookId)) {
        state.keywordBuilderRefreshNeeded = false;
        state.catalogFeedback = "Saved";
        state.catalogFeedbackError = false;
      }
      if (catalogWasAwaiting) {
        if (catalogCommand === "book.metadata.save") { state.bookMetadataDrafts.delete(catalogTarget); state.bookMetadataValidation.delete(catalogTarget); }
        if (catalogCommand === "brand.author.save") state.brandAuthorDrafts.delete(catalogTarget);
        state.catalogMutationAwaitingSnapshot = false;
        state.catalogFeedback = "Saved";
        state.catalogFeedbackError = false;
      }
      state.applicationLoadState = "ready";
      state.applicationLoadError = "";
      state.libraryRefreshTaskId = "";
      state.libraryRefreshResultRequested = false;
      if (state.keywordBuilderRefreshPending) {
        state.keywordBuilderRefreshPending = false;
        state.keywordBuilderRefreshNeeded = state.keywordBuilderConfirmed.has(state.keywordBuilderRefreshBookId);
        state.catalogFeedback = state.keywordBuilderRefreshNeeded ? "Saved. Library refresh did not include the latest build; retry refresh." : "Saved";
        state.catalogFeedbackError = false;
      }
      const allBrands = valueFor(discovery(), "brands", []);
      if (!allBrands.some((brand) => valueFor(brand, "name", "") === state.inspectedBrand)) state.inspectedBrand = valueFor(allBrands[0], "name", "");
      if (bookCloneWasAwaiting && currentRoute() === "books") {
        render("books", false);
        status.textContent = state.bookCloneNotice ? "Book clone ready" : "Book clone refresh needs attention";
        window.requestAnimationFrame?.(() => document.querySelector("#book-detail-title")?.focus?.());
      } else if (cloneWasAwaiting && currentRoute() === "brands") {
        render("brands", false);
        status.textContent = state.brandCloneNotice ? "Brand clone ready for validation" : "Brand clone refresh needs attention";
      } else if (preserveBookDrawer) {
        if (state.selectedBookTab === "artwork") refreshInteriorArtworkWorkspace();
        else updateInteriorSaveUi();
        refreshBookListRow(state.selectedBookId);
        status.textContent = interiorShuffleWasAwaiting ? "Interior order randomized" : "Interior changes saved";
      } else if (preserveProductionDrawer) {
        state.productionFeedback = state.productionFeedback.replace(" Refreshing status…", "");
        refreshProductionWorkspace(state.productionFocusSelector);
        state.productionFocusSelector = "";
        refreshBookListRow(state.selectedBookId);
        updateGlobalRefreshControl();
        status.textContent = catalogWasAwaiting ? "Catalog changes saved" : "Connected";
      } else if (preserveCatalogDrawer && catalogCommand === "book.completion.set") {
        render("books", false);
        updateGlobalRefreshControl();
        status.textContent = "Book completion saved";
        window.requestAnimationFrame?.(() => document.querySelector('[data-action="set-book-completion"]')?.focus?.());
      } else if (preserveCatalogDrawer) {
        refreshBrandDependentBookUi(catalogCommand);
        refreshBookListRow(state.selectedBookId);
        updateGlobalRefreshControl();
        status.textContent = "Catalog changes saved";
      } else if (preserveKeywordDrawer) {
        refreshBookKeywordBuilderCard();
        refreshBookListRow(state.selectedBookId);
        updateGlobalRefreshControl();
        status.textContent = state.keywordBuilderRefreshNeeded ? "Keyword Builder saved; refresh needed" : "Keyword Builder saved";
      } else {
        render(document.querySelector(".nav-item-active")?.dataset.route ?? "books", false);
        status.textContent = "Connected";
      }
    } else if (ok && command === "cache.cleanup.result") {
      const result = valueFor(response, "payload", {});
      const cleaned = valueFor(result, "cleanedBooks", 0);
      const skipped = valueFor(result, "skippedBooks", 0);
      const failed = valueFor(result, "failedBooks", 0);
      const freed = fileSize(valueFor(result, "freedBytes", 0));
      state.cacheCleanupTaskId = "";
      state.cacheCleanupResultRequested = false;
      state.cacheCleanupActive = false;
      status.textContent = `Cleared ${cleaned} Books • Freed ${freed}` + (skipped ? ` • ${skipped} skipped` : "") + (failed ? ` • ${failed} failed` : "");
      beginApplicationRefresh();
    } else if (ok && command === "settings.saved") {
      window.appSnapshot = { ...(window.appSnapshot ?? {}), globalSettings: valueFor(response, "payload", {}) };
      state.settingsGenericKeywordDraftsInitialized = false;
      state.settingsAmazonProfileDraftsInitialized = false;
      state.settingsSavePending = false;
      state.settingsFeedback = "Saved";
      state.settingsFeedbackError = false;
      if (currentRoute() === "configuration") render("configuration", false);
      status.textContent = "Settings saved";
    } else if (ok && command === "process.snapshot") {
      const snapshot = valueFor(response, "payload", {});
      if (isStaleProcessSnapshot(snapshot)) return;
      window.processSnapshot = snapshot;
      state.processStartPending = false;
      const startedAt = valueFor(window.processSnapshot, "startedAt", "");
      const terminal = !valueFor(window.processSnapshot, "isActive", false) && !valueFor(window.processSnapshot, "isCancelling", false);
      const productionSession = processMode(window.processSnapshot) === "production-interior";
      if (terminal && startedAt && state.lastTerminalRefreshSession !== startedAt) {
        state.lastTerminalRefreshSession = startedAt;
        if (productionSession && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
          state.productionRefreshAwaitingSnapshot = true;
        }
        beginApplicationRefresh();
      }
      updateGlobalProcessStatus();
      if (document.querySelector(".nav-item-active")?.dataset.route === "process") {
        render("process", false);
        if (terminal) window.requestAnimationFrame?.(() => document.querySelector("[data-process-failure-summary]")?.focus?.());
      }
      if (state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
        state.productionFeedback = productionSession && valueFor(window.processSnapshot, "isActive", false)
          ? valueFor(window.processSnapshot, "currentStep", "Building Final Interior…")
          : state.productionFeedback;
        updateProductionInteractionUi();
      }
      if (terminal) state.productionFinalBuildActive = false;
      status.textContent = "Connected";
    } else if (ok && command === "brand.validation.result") {
      state.brandValidationResult = { ...valueFor(response, "payload", {}), brandName: validationRequestBrand };
      if (currentRoute() === "brands") render("brands", false);
      content.querySelector?.("[data-brand-validation-summary]")?.focus?.();
      status.textContent = valueFor(state.brandValidationResult, "isSuccess", false) ? "Brand validation completed" : "Brand validation needs attention";
      beginApplicationRefresh();
    } else if (ok && command === "book.brand.templates.copied") {
      state.brandTemplateCopyPending = false;
      if (state.bookDrawerOpen && currentRoute() === "books") refreshBookDrawerBody();
      status.textContent = "Brand templates copied";
    } else if (ok && command === "book.production.pdf-name-suggestions") {
      const names = valueFor(response, "payload", {});
      const nameBookId = String(valueFor(names, "bookId", valueFor(productionPdfNameRequest, "bookId", "")) ?? "");
      if (nameBookId) {
        state.productionPdfNames.set(nameBookId, names);
        state.productionPdfNamePending.delete(nameBookId);
        state.productionPdfNameErrors.delete(nameBookId);
        if (state.bookDrawerOpen && state.selectedBookTab === "production" && state.selectedBookId === nameBookId) {
          refreshProductionWorkspace(valueFor(productionPdfNameRequest, "regenerate", false) ? '[data-action="randomize-pdf-names"]' : "");
        }
      }
      status.textContent = "PDF filename suggestions ready";
    } else if (ok && command === "book.production.asset.import.cancelled") {
      const assetKind = state.productionImportPending;
      state.productionImportPending = "";
      state.productionFeedback = "";
      state.productionFeedbackError = false;
      state.productionFeedbackWarning = false;
      if (state.bookDrawerOpen && state.selectedBookTab === "production") {
        updateProductionInteractionUi();
        document.querySelector(`[data-action="upload-production-asset"][data-production-asset="${assetKind}"]`)?.focus();
      }
      state.productionFocusSelector = "";
    } else if (ok && command === "book.production.asset.imported") {
      state.productionImportPending = "";
      state.productionFeedback = "Production PNG imported. Refreshing status…";
      state.productionFeedbackError = false;
      state.productionFeedbackWarning = false;
      if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      state.productionRefreshAwaitingSnapshot = true;
      beginApplicationRefresh();
    } else if (ok && command === "book.output.action.completed") {
      const fallback = valueFor(valueFor(response, "payload", {}), "fallbackToOriginal", false);
      const message = fallback
        ? "Lightweight preview unavailable; opened the original PDF."
        : requestCommand === "book.output.open-folder"
          ? "Opened the Book output folder."
          : "Opened the PDF preview.";
      setPdfLibraryFeedback(message);
      status.textContent = message;
    } else if (ok && command === "book.interior.folder.opened") {
      state.interiorFolderOpenPending = false;
      state.interiorFolderOpenBookId = "";
      if (state.bookDrawerOpen && state.selectedBookTab === "artwork") refreshInteriorArtworkWorkspace();
      status.textContent = "Opened the Book interior folder.";
    } else if (ok && command === "diagnostics.snapshot") {
      window.uiDiagnostics = valueFor(response, "payload", []);
      if (currentRoute() === "diagnostics") render("diagnostics", false);
      status.textContent = "Diagnostics refreshed";
    } else if (ok && command === "background.tasks") {
      state.backgroundTasks = valueFor(response, "payload", []);
      if (currentRoute() === "diagnostics") render("diagnostics", false);
    } else {
      if (requestCommand.startsWith("s3.") || requestCommand.startsWith("book.s3.")) {
        state.storageLoading = false;
        state.storageSettingsPending = false;
        if (storageRequestBookId) state.storagePendingBooks.delete(storageRequestBookId);
        const error = String(valueFor(response, "error", "storage_request_failed")).split(":", 1)[0];
        const message = storageErrorMessage(error);
        state.storageFeedback = message;
        state.storageFeedbackError = true;
        if (currentRoute() === "configuration") renderConfiguration();
        if (state.selectedBookId) patchBookS3(state.selectedBookId);
        status.textContent = "S3 Storage needs attention";
        return;
      }
      if (requestCommand.startsWith("updates.")) {
        state.updateCommandPending = "";
        updateUpdateControls();
        stopUpdatePolling();
      }
      const error = valueFor(response, "error", "unexpected response");
      if (requestCommand === "book.production.pdf-name-suggestions.get") {
        const nameBookId = String(valueFor(productionPdfNameRequest, "bookId", state.selectedBookId) ?? "");
        state.productionPdfNamePending.delete(nameBookId);
        state.productionPdfNameErrors.set(nameBookId, productionPdfNameErrorMessage(error));
        if (state.bookDrawerOpen && state.selectedBookTab === "production" && state.selectedBookId === nameBookId) {
          refreshProductionWorkspace('[data-action="randomize-pdf-names"]');
        }
        status.textContent = "PDF filename suggestions need attention";
        return;
      }
      if (requestCommand === "amazon.browser.open" || requestCommand === "amazon.browser.status") {
        state.amazonBrowserPending = false;
        state.amazonBrowserStatus = { state: "Error", reasonCode: error };
        const message = ({
          cloak_browser_download_failed: "Browser components could not be downloaded. Check network, disk space, and antivirus, then retry.",
          cloak_browser_license_required: "CloakBrowser needs a valid free access key or license.",
          cloak_browser_license_invalid: "The CloakBrowser access key or license is invalid or expired. Update it and retry.",
          cloak_browser_profile_locked: "The Amazon browser profile is already in use. Close the other browser or app instance and retry.",
          browser_storage_not_writable: "The app cannot write the .cloakbrowser profile and cache folders.",
          amazon_asin_crawl_active: "An ASIN crawl is using the Amazon browser. Wait for it to finish or cancel it from its Book."
        })[String(error)] ?? "Amazon browser could not be opened. Check the setup and retry.";
        setAsinFeedback(state.selectedBookId, message, true);
        patchAsinResearch(state.selectedBookId);
        status.textContent = "Amazon browser needs attention";
        return;
      }
      if (requestCommand.startsWith("book.keywords.asin-crawl.")) {
        if (requestCommand === "book.keywords.asin-crawl.start") {
          const researchDraft = state.asinResearchDrafts.get(state.selectedBookId);
          if (researchDraft) researchDraft.autoApplyPending = false;
        }
        const message = error === "amazon_asin_crawl_active"
          ? "Another ASIN crawl is already running. Wait for it to finish or cancel it from its Book."
          : error === "amazon_keywords_required" ? "Enter at least one Search Keyword."
            : error === "amazon_keywords_too_many" ? "Search Keywords accepts at most 30 phrases."
              : error === "amazon_keyword_too_long" ? "Each Search Keyword must be at most 200 characters."
                : "ASIN crawl could not start. Check the browser and retry.";
        setAsinFeedback(state.selectedBookId, message, true);
        patchAsinResearch(state.selectedBookId);
        status.textContent = "ASIN crawl needs attention";
        return;
      }
      if (pdfLibraryAction) {
        const message = pdfLibraryActionError(String(error).split(":", 1)[0]);
        setPdfLibraryFeedback(message, true);
        status.textContent = message;
        return;
      }
      if (["app.refresh", "app.refresh.result", "task.get"].includes(requestCommand) && state.keywordBuilderRefreshPending) {
        state.keywordBuilderRefreshPending = false;
        state.keywordBuilderRefreshNeeded = true;
        state.catalogFeedback = "Saved. Library refresh failed; retry refresh.";
        state.catalogFeedbackError = false;
        state.applicationLoadState = "ready";
        state.applicationLoadError = "";
        state.libraryRefreshTaskId = "";
        state.libraryRefreshResultRequested = false;
        if (currentRoute() === "books" && state.bookDrawerOpen) refreshBookKeywordBuilderCard();
        status.textContent = "Keyword Builder saved; refresh needed";
        return;
      }
      if (requestCommand === "book.brand.templates.copy") {
        state.brandTemplateCopyPending = false;
        if (state.bookDrawerOpen && currentRoute() === "books") refreshBookDrawerBody();
      }
      if (requestCommand === "brand.clone") {
        state.brandClonePending = false;
        state.brandCloneAwaitingSnapshot = false;
        state.brandCloneFeedback = brandCloneErrorMessage(error);
        state.brandCloneFeedbackError = true;
        if (currentRoute() === "brands") render("brands", false);
        status.textContent = "Brand clone needs attention";
        return;
      }
      if (requestCommand === "book.clone") {
        state.bookClonePending = false;
        state.bookCloneAwaitingSnapshot = false;
        state.bookCloneFeedback = bookCloneErrorMessage(error);
        state.bookCloneFeedbackError = true;
        if (currentRoute() === "books") render("books", false);
        status.textContent = "Book clone needs attention";
        return;
      }
      if (requestCommand === "book.interior.settings.save") {
        state.bookInteriorSavePending = false;
        updateInteriorSaveUi();
      }
      if (requestCommand === "book.interior.shuffle") {
        state.interiorShufflePending = false;
        const message = ({
          processing_active: "Wait for Interior processing to finish.",
          production_action_active: "Wait for the active Production action to finish.",
          cache_cleanup_active: "Wait for Clear Cache to finish.",
          workspace_state_unavailable: "Repair the Book workspace state, then refresh and retry.",
          interior_shuffle_no_active_pages: "Activate at least one Interior page before randomizing.",
          interior_shuffle_source_invalid: "The Book interior source is unavailable or invalid."
        })[String(error)] ?? "Interior order could not be randomized. Refresh and retry.";
        state.interiorShuffleFeedback = message;
        state.interiorShuffleFeedbackError = true;
        if (state.bookDrawerOpen && state.selectedBookTab === "artwork") refreshInteriorArtworkWorkspace();
        status.textContent = "Random Interior needs attention";
        return;
      }
      if (requestCommand === "book.interior.open-folder") {
        state.interiorFolderOpenPending = false;
        state.interiorFolderOpenBookId = "";
        if (state.bookDrawerOpen && state.selectedBookTab === "artwork") refreshInteriorArtworkWorkspace();
        status.textContent = ({
          invalid_interior_folder_action: "The Interior folder action was invalid. Refresh and try again.",
          interior_folder_not_found: "The Book interior folder does not exist.",
          interior_folder_launch_failed: "Windows could not open the Book interior folder. Check folder permissions and try again."
        })[String(error)] ?? "The Book interior folder could not be opened.";
        return;
      }
      if (requestCommand === "settings.save") {
        state.settingsSavePending = false;
        state.settingsFeedback = error === "invalid_settings" ? "Review the configuration values and try again." : "Settings could not be saved. Try again.";
        state.settingsFeedbackError = true;
        updateSettingsSaveUi();
        status.textContent = "Settings could not be saved";
        return;
      }
      if (["book.completion.set", "book.metadata.save", "book.keywords.shuffle", "book.keywords.preview.open", "book.keywords.preview.update-ads-asin", "book.keywords.save", "book.brand.assign", "book.brand.unassign", "brand.author.save"].includes(requestCommand)) {
        state.catalogMutationPending = false;
        state.catalogMutationAwaitingSnapshot = false;
        if (requestCommand.startsWith("book.keywords.")) state.keywordBuilderPending.delete(state.catalogMutationTarget || state.selectedBookId);
        if (requestCommand === "book.keywords.preview.update-ads-asin") {
          const researchDraft = state.asinResearchDrafts.get(state.catalogMutationTarget || state.selectedBookId);
          if (researchDraft) researchDraft.appliedRevision = -1;
        }
        if (requestCommand === "book.keywords.save") state.keywordBuilderSubmitted = null;
        const responsePayload = valueFor(response, "payload", {});
        const backendErrors = valueFor(responsePayload, "validationErrors", []);
        const structuredMetadataErrors = requestCommand === "book.metadata.save" && error === "invalid_book_metadata" && valueFor(responsePayload, "policyVersion", 0) === 1 && Array.isArray(backendErrors)
          ? backendErrors.map((item) => ({
            field: String(valueFor(item, "field", "")),
            code: String(valueFor(item, "code", "")),
            message: String(valueFor(item, "message", "")),
            ...(Array.isArray(valueFor(item, "tokens", null)) ? { tokens: valueFor(item, "tokens", []).map(String) } : {})
          })).filter((item) => metadataFieldOrder.includes(item.field) && item.code && item.message)
          : [];
        const structuredKeywordError = requestCommand.startsWith("book.keywords.") && valueFor(responsePayload, "policyVersion", 0) === 1 && valueFor(responsePayload, "field", "") === "keywords"
          ? { code: String(valueFor(responsePayload, "code", error)), message: String(valueFor(responsePayload, "message", "")) }
          : null;
        if (structuredMetadataErrors.length) {
          state.bookMetadataValidation.set(state.catalogMutationTarget, { attempted: true, errors: structuredMetadataErrors });
          state.catalogFeedback = metadataValidationSummary(structuredMetadataErrors);
        } else if (structuredKeywordError?.message) {
          state.bookKeywordBuilderValidation.set(state.catalogMutationTarget, structuredKeywordError);
          state.catalogFeedback = structuredKeywordError.message;
        } else {
          state.catalogFeedback = catalogErrorMessage(error);
        }
        state.catalogFeedbackError = true;
        if (currentRoute() === "books" && state.bookDrawerOpen) {
          updateBookCatalogMutationUi();
          if (structuredMetadataErrors.length) {
            const book = selectedBook();
            const summary = book ? summaryFor(book) : null;
            if (book && summary) patchBookMetadataValidationUi(book, summary, true);
          }
          if (requestCommand.startsWith("book.keywords.")) refreshBookKeywordBuilderCard(requestCommand === "book.keywords.shuffle");
        }
        if (currentRoute() === "brands") render("brands", false);
      }
      if (requestCommand === "book.production.asset.import") {
        state.productionImportPending = "";
        state.productionFeedback = error === "production_cover_size_invalid"
          ? "Final Cover must be exactly 5242 × 2626 px. The previous asset was kept."
          : "The Production asset could not be imported. The previous asset was kept.";
        state.productionFeedbackError = true;
        state.productionFeedbackWarning = false;
        if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      }
      if (requestCommand === "book.production.action.start") {
        state.productionActionName = "";
        state.productionFeedback = error === "production_action_active"
          ? "Another Production action is already running."
          : error === "processing_active"
            ? "Interior Processing is running."
            : error === "cache_cleanup_active"
              ? "Clear Cache is running."
              : "Production action could not start. The previous asset or PDF was kept.";
        state.productionFeedbackError = true;
        state.productionFeedbackWarning = false;
        if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
      }
      if (requestCommand === "app.refresh") {
        if (state.productionRefreshAwaitingSnapshot && state.bookDrawerOpen && state.selectedBookTab === "production" && currentRoute() === "books") {
          state.productionRefreshAwaitingSnapshot = false;
          state.applicationLoadState = window.appSnapshot ? "ready" : "idle";
          state.applicationLoadError = "";
          state.productionFeedback = "Production status refresh failed. The completed file was kept; use Refresh to retry.";
          state.productionFeedbackError = true;
          state.productionFeedbackWarning = false;
          updateGlobalRefreshControl();
          updateProductionInteractionUi();
          status.textContent = "Production status refresh failed";
          return;
        }
        if (error === "cache_cleanup_active") {
          state.applicationLoadState = window.appSnapshot ? "ready" : "idle";
          state.applicationLoadError = "";
          status.textContent = "Clear Cache is running";
          render(currentRoute(), false);
          return;
        }
        state.applicationLoadState = "failed";
        state.applicationLoadError = error;
        render(currentRoute(), false);
      }
      if (requestCommand === "cache.clear" && ["cache_cleanup_processing_active", "cache_cleanup_refresh_active", "cache_cleanup_production_active"].includes(error)) {
        state.cacheCleanupTaskId = "";
        state.cacheCleanupResultRequested = false;
        state.cacheCleanupActive = false;
        status.textContent = error === "cache_cleanup_processing_active" ? "Interior Processing is running" : error === "cache_cleanup_production_active" ? "A Production action is running" : "Library refresh is running";
        if (currentRoute() === "books") render("books", false);
        return;
      }
      if (["book.background.set", "book.interior.active.set", "book.interior.settings.save"].includes(requestCommand) && error === "processing_active") {
        status.textContent = "Interior Processing is running";
        updateInteriorSaveUi();
        if (currentRoute() === "books") render("books", false);
        return;
      }
      if (requestCommand === "process.start") {
        const productionBuildFailedToStart = state.productionFinalBuildActive;
        state.processStartPending = false;
        state.productionFinalBuildActive = false;
        if (error === "cache_cleanup_active") {
          status.textContent = "Clear Cache is running";
          return;
        }
        if (error === "production_action_active") {
          state.productionFeedback = "Wait for the active Production action to finish.";
          state.productionFeedbackError = true;
          state.productionFeedbackWarning = false;
          if (state.bookDrawerOpen && state.selectedBookTab === "production") updateProductionInteractionUi();
          return;
        }
        if (productionBuildFailedToStart && state.bookDrawerOpen && state.selectedBookTab === "production") {
          state.productionFeedback = "Final Interior could not start. The previous Interior PDF was kept.";
          state.productionFeedbackError = true;
          state.productionFeedbackWarning = false;
          updateProductionInteractionUi();
        }
      }
      status.textContent = `Bridge error: ${error}`;
    }
  });

  const refreshButton = document.getElementById("refresh-button");
  if (refreshButton) refreshButton.addEventListener("click", beginApplicationRefresh);
  document.getElementById("update-dialog-root")?.addEventListener("click", (event) => {
    const action = event.target.closest("[data-update-action]")?.dataset.updateAction;
    if (action) beginUpdateAction(action);
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !updateIsBusy() && !document.getElementById("update-dialog-root")?.hidden) dismissUpdateDialog();
  });
  const globalProcessStatus = document.getElementById("global-process-status");
  if (globalProcessStatus) globalProcessStatus.addEventListener("click", () => {
    const activeBookId = String(valueFor(state.storageSnapshot, "activeBookId", "") ?? "");
    if (!activeBookId) { render("process"); return; }
    state.selectedBookId = activeBookId;
    state.selectedBookTab = "settings";
    state.bookDrawerOpen = true;
    render("books");
  });
  updateGlobalProcessStatus();
  window.setInterval(() => { if (valueFor(window.processSnapshot, "isActive", false) || valueFor(window.processSnapshot, "isCancelling", false)) send("process.get"); }, 1000);
  send("app.ping");
  beginUpdateCheck();
  state.applicationLoadState = "loading";
  render("books", false);
  send("app.refresh");
})();
