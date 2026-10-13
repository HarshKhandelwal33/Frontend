"""Local rosbridge protocol test peer; not a ROS implementation.

Run alongside GearboxRosChecks.RunBatch. Exercises real Unity WebSocket traffic.
"""
import asyncio
import json
from pathlib import Path
import websockets

ROOT = Path(__file__).resolve().parents[1]
REPORT = ROOT / '.simulation-check' / 'ros-protocol.json'


async def main():
    done = asyncio.Event()
    result = dict(passed=False, advertisements=[], acknowledgements=[], stages=[], joint_messages=0)

    async def peer(socket):
        stage = 'connect'
        async def command(action, request_id):
            payload = json.dumps(dict(action=action, request_id=request_id))
            await socket.send(json.dumps(dict(op='publish', topic='/gearbox/simulation/control', msg=dict(data=payload))))
        try:
            async for raw in socket:
                message = json.loads(raw)
                if message['op'] in ('advertise', 'subscribe'):
                    result['advertisements'].append(message)
                if message['op'] != 'publish':
                    continue
                topic = message['topic']
                if topic.endswith('/joint_states'):
                    joints = message['msg']
                    assert len(joints['name']) == len(joints['position']) == 6
                    assert 0 <= joints['header']['stamp']['nanosec'] < 1_000_000_000
                    result['joint_messages'] += 1
                    continue
                payload = json.loads(message['msg']['data'])
                if topic.endswith('/control_ack'):
                    result['acknowledgements'].append(payload)
                    continue
                if not topic.endswith('/state'):
                    continue
                if payload.get('fault'):
                    raise AssertionError(payload['fault'])
                if stage == 'connect':
                    await command('invalid', 'invalid-1')
                    await command('ready', 'ready-1')
                    await command('ready', 'ready-1')
                    stage = 'ready'
                elif stage == 'ready' and payload['running']:
                    result['stages'].append('ready_started')
                    await command('pause', 'pause-1')
                    stage = 'pause'
                elif stage == 'pause' and payload['paused']:
                    result['stages'].append('paused')
                    await command('resume', 'resume-1')
                    stage = 'resume'
                elif stage == 'resume' and payload['running'] and not payload['paused']:
                    result['stages'].append('resumed')
                    await command('reset', 'reset-1')
                    stage = 'reset'
                elif stage == 'reset' and not payload['running'] and not payload['paused'] and payload['completed'] == 0:
                    result['stages'].append('reset')
                    acknowledgements = {x['request_id']: x['status'] for x in result['acknowledgements']}
                    assert acknowledgements['invalid-1'] == 'rejected'
                    assert any(x['status'] == 'duplicate' for x in result['acknowledgements'])
                    assert all(acknowledgements[x] == 'dispatched' for x in ('pause-1', 'resume-1', 'reset-1'))
                    assert result['joint_messages'] > 0
                    result['passed'] = True
                    break
        except Exception as error:
            result['error'] = str(error)
        finally:
            REPORT.parent.mkdir(exist_ok=True)
            REPORT.write_text(json.dumps(result, indent=2), encoding='utf-8')
            done.set()

    REPORT.parent.mkdir(exist_ok=True)
    if REPORT.exists():
        REPORT.unlink()
    async with websockets.serve(peer, '127.0.0.1', 9091):
        print('Local protocol test listening on 9091', flush=True)
        try:
            await asyncio.wait_for(done.wait(), timeout=240)
        except asyncio.TimeoutError:
            result['error'] = 'Unity did not complete the protocol test within 240 seconds'
            REPORT.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result), flush=True)


if __name__ == '__main__':
    asyncio.run(main())
