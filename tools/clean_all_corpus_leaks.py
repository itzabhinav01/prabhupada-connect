import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')

db_path = r'C:\VedaBaseModern2\Database\prabhupada_corpus.db'
conn = sqlite3.connect(db_path)
cur = conn.cursor()

print("=== STARTING COMPREHENSIVE CORPUS RECTIFICATION ===")

# Track statistics
stats = {
    'split_words': 0,
    'sb_canto_ends': 0,
    'cc_chapter_ends': 0,
    'sb_chapter_ends': 0,
    'folio_bookmarks': 0,
    'records_updated': 0
}

# Fetch all records
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records")
records = cur.fetchall()
print(f"Total corpus records loaded: {len(records)}")

split_word_rules = [
    (re.compile(r'\bcaritāmṛ\s+ta\b'), 'caritāmṛta'),
    (re.compile(r'\bAm\s+ṛta\b'), 'Amṛta'),
    (re.compile(r'\bam\s+ṛta\b'), 'amṛta'),
    (re.compile(r'\bBhaktive\s+danta\b'), 'Bhaktivedanta'),
    (re.compile(r'\bBhakti\s+vedanta\b'), 'Bhaktivedanta'),
    (re.compile(r'\bB\s+ṛhaspati\b'), 'Bṛhaspati'),
    (re.compile(r'\bafflic\s+ted\b'), 'afflicted'),
]

bookmark_regex = re.compile(r'\\\*[^\*\n]{1,80}\\\*')

for rkey, bkey, ref, trans, purp in records:
    orig_trans = trans
    orig_purp = purp
    new_trans = trans
    new_purp = purp

    # Step 1: Split word healing
    for pat, rep in split_word_rules:
        if new_trans and pat.search(new_trans):
            new_trans = pat.sub(rep, new_trans)
            stats['split_words'] += 1
        if new_purp and pat.search(new_purp):
            new_purp = pat.sub(rep, new_purp)
            stats['split_words'] += 1

    # Step 2: SB Canto Ends
    if bkey == 'SB':
        for fld in ['trans', 'purp']:
            val = new_trans if fld == 'trans' else new_purp
            if val and 'END OF THE' in val:
                m = re.search(r'(END OF THE [A-Z]+ CANTO)\b(.*)$', val, re.DOTALL)
                if m and m.group(2).strip():
                    cleaned_val = val[:m.end(1)].strip()
                    if fld == 'trans':
                        new_trans = cleaned_val
                    else:
                        new_purp = cleaned_val
                    stats['sb_canto_ends'] += 1

    # Step 3: CC Chapter Ends (ĀDI, MADHYA, ANTYA)
    if bkey in ('ĀDI', 'MADHYA', 'ANTYA'):
        for fld in ['trans', 'purp']:
            val = new_trans if fld == 'trans' else new_purp
            if val and 'Thus end the Bhaktivedanta purports to' in val:
                # Match through the end of "... Chapter, describing ... ."
                m = re.search(r'(Thus end the Bhaktivedanta purports to Śrī Caitanya-caritāmṛta[^\.]*Chapter[^\.]*\.)\s*(.*)$', val, re.IGNORECASE | re.DOTALL)
                if m:
                    tail = m.group(2).strip()
                    if tail and not tail.startswith('END OF'):
                        cleaned_val = val[:m.end(1)].strip()
                        if fld == 'trans':
                            new_trans = cleaned_val
                        else:
                            new_purp = cleaned_val
                        stats['cc_chapter_ends'] += 1

    # Step 4: SB Chapter Ends
    if bkey == 'SB':
        for fld in ['trans', 'purp']:
            val = new_trans if fld == 'trans' else new_purp
            if val and 'Thus end the Bhaktivedanta purports' in val:
                m = re.search(r'(Thus end the Bhaktivedanta purports\b.*?[\.\?]["\']?)\s*(SB\s+\d+.*)$', val, re.IGNORECASE | re.DOTALL)
                if m:
                    cleaned_val = val[:m.end(1)].strip()
                    if fld == 'trans':
                        new_trans = cleaned_val
                    else:
                        new_purp = cleaned_val
                    stats['sb_chapter_ends'] += 1

    # Step 5: Clean remaining Folio bookmark tags
    if new_trans and r'\*' in new_trans:
        subbed = bookmark_regex.sub('', new_trans)
        if subbed != new_trans:
            new_trans = re.sub(r'[ \t]+', ' ', subbed).strip()
            stats['folio_bookmarks'] += 1

    if new_purp and r'\*' in new_purp:
        subbed = bookmark_regex.sub('', new_purp)
        if subbed != new_purp:
            new_purp = re.sub(r'[ \t]+', ' ', subbed).strip()
            stats['folio_bookmarks'] += 1

    # If changes made, update Records and SearchIndex
    if new_trans != orig_trans or new_purp != orig_purp:
        cur.execute("UPDATE Records SET Translation = ?, Purports = ? WHERE RecordKey = ?", (new_trans, new_purp, rkey))
        cur.execute("UPDATE SearchIndex SET Translation = ?, Purports = ? WHERE RecordKey = ?", (new_trans, new_purp, rkey))
        stats['records_updated'] += 1

conn.commit()
conn.close()

print("\n=== RECTIFICATION COMPLETE ===")
for k, v in stats.items():
    print(f"  {k}: {v}")
