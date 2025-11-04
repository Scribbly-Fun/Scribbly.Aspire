import http from 'k6/http';
import { sleep } from 'k6';

const ASPIRE_RESOURCE = __ENV.ASPIRE_RESOURCE;

export let options = {
  vus: 10,
  duration: '30s',
};

export default function () {
  http.get(`${ASPIRE_RESOURCE}/weatherforecast`);
  sleep(1);
}
