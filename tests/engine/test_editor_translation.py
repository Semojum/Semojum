import copy
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts'))
from editor_translation import PageTranslationCache
from semojum_braille import sidecar


class TranslationCacheTests(unittest.TestCase):
    def request(self, texts):
        return dict(op='translate_page', elements=[dict(id=str(i), text=text, type='text', order=i) for i, text in enumerate(texts)])

    def test_one_edit_retranslates_only_changed_element(self):
        request = self.request(['서로 다른 문장 ' + str(i) for i in range(12)])
        changed = copy.deepcopy(request)
        changed['elements'][5]['text'] += ' 수정'
        with patch.object(sidecar, '_run', wraps=sidecar._run) as run:
            sidecar.handle(request)
            run.reset_mock()
            sidecar.handle(changed)
            self.assertEqual(12, run.call_count)  # Reproduction without the desktop cache.
        cache = PageTranslationCache(sidecar)
        with patch.object(sidecar, '_run', wraps=sidecar._run) as run:
            cache.handle(request, sidecar.handle)
            run.reset_mock()
            result = cache.handle(changed, sidecar.handle)
            self.assertEqual(1, run.call_count)
        self.assertEqual(sidecar.handle(changed), result)

    def test_context_and_options_keep_upstream_results(self):
        cache = PageTranslationCache(sidecar)
        request = self.request(['제목', '설명', '다음 그림', '본문', '<!표><!행><!칸>가<!칸>나<!/행><!/표>', '<!쪽바꿈>', '', '끝'])
        request['elements'][0].update(type='heading', heading_level=3)
        request['elements'][1]['type'] = 'image'
        request['elements'][2]['type'] = 'diagram'
        request['elements'][4]['type'] = 'table'
        for case in range(8):
            current = copy.deepcopy(request)
            if case == 1: current['elements'].reverse()
            if case == 2: current['elements'][2]['type'] = 'text'
            if case == 3: current['elements'][0]['heading_level'] = 1
            if case == 4: current['korean_grade'] = 1
            if case == 5: current['math_page'] = True
            if case == 6: current['split_choices'] = True
            if case == 7: current['elements'][4]['render_mode'] = 'table_grid'
            expected = sidecar.handle(current)
            self.assertEqual(expected, cache.handle(current, sidecar.handle))
            self.assertEqual(expected, cache.handle(current, sidecar.handle))

    def test_blocked_results_and_oversized_entries_are_not_reused(self):
        request = self.request(['재시도할 문장'])
        cache = PageTranslationCache(sidecar)
        with patch.object(sidecar, 'is_blocked_braille', return_value=True):
            with patch.object(sidecar, '_run', wraps=sidecar._run) as run:
                cache.handle(request, sidecar.handle)
                cache.handle(request, sidecar.handle)
                self.assertEqual(2, run.call_count)
        self.assertEqual(0, len(cache.entries))
        tiny = PageTranslationCache(sidecar, max_bytes=1)
        self.assertEqual(sidecar.handle(request), tiny.handle(request, sidecar.handle))
        self.assertEqual(0, len(tiny.entries))
        self.assertEqual(0, tiny.size)
    def test_duplicate_slots_mutation_eviction_and_error_restore(self):
        cache = PageTranslationCache(sidecar, max_entries=2)
        request = self.request(['같은 문장', '같은 문장'])
        for element in request['elements']: element['order'] = 0
        expected = sidecar.handle(request)
        result = cache.handle(request, sidecar.handle)
        result['elements'][0]['cells'] = 'corrupt'
        self.assertEqual(expected, cache.handle(request, sidecar.handle))
        cache.handle(self.request(['다른 내용', '새 내용', '마지막']), sidecar.handle)
        self.assertLessEqual(len(cache.entries), 2)
        self.assertLessEqual(cache.size, cache.max_bytes)
        original = sidecar.translate
        with self.assertRaises(Exception):
            cache.handle(dict(op='translate_page', elements=[dict(text='가' * 1001)]), sidecar.handle)
        self.assertIs(original, sidecar.translate)
        self.assertEqual(expected, cache.handle(request, sidecar.handle))


if __name__ == '__main__':
    unittest.main()