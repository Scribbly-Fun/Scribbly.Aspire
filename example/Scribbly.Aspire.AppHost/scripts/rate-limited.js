import http from 'k6/http';
import { check, sleep } from 'k6';

const ASPIRE_RESOURCE = __ENV.ASPIRE_RESOURCE;

export let options = {
  vus: 10,
  duration: '30s',
};

export default function () {
  const res = http.get(`${ASPIRE_RESOURCE}/load-test`);
  check(res, {
    'response code was 200': (res) => res.status == 200,
  });
  sleep(1);
}
