
import { useContext } from 'react';
import './Team.css'
import { TeamsContext } from '../Store/TeamsContext';
import { Button, Col, List, Popconfirm, Row, Skeleton } from 'antd';
import { QuestionCircleOutlined, UserOutlined } from '@ant-design/icons';

function Team(props) {
    const teamsContext = useContext(TeamsContext);


    function getTeamsExcludingThis(teamId) {
        return teamsContext.teams.filter(t => t.teamId != teamId)
    }


    function moveToOtherTeam(fromTeam, toTeam, player) {
        teamsContext.movePlayer(fromTeam, toTeam, player)
    }

    function removePlayer(fromTeam, player) {
        teamsContext.removePlayer(fromTeam, player)
    }

    return (
        <>

            {props.team.players.map(player =>
                <div>
                    <Row >
                        <Col style={{ width: '100px', 'text-overflow': 'ellipsis', 'white-space': 'nowrap', 'overflow': 'hidden' , margin: '4px' }} flex='auto'>
                            {player.name}
                        </Col>

                        <Col flex='30px'>
                            <Popconfirm title="Remove player" onConfirm={() => { removePlayer(props.team, player) }}
                                description={"Are you sure to remove " + player.name + "?"}
                                icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                                <Button style={{margin: '4px'}} size='small' type="primary" danger>-</Button>
                            </Popconfirm>
                        </Col>

                        {getTeamsExcludingThis(props.team.teamId).map((team) =>
                            <Popconfirm title="Move player" onConfirm={() => { moveToOtherTeam(props.team, team, player) }}
                                description={"Are you sure to move " + player.name + " to team " + team.teamId}
                                icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                                <Button style={{margin: '4px'}} size='small' disabled={props.team.players.length === 1} className="move-to-group" >{team.teamId}</Button>
                            </Popconfirm>
                        )}
                    </Row>
                </div>
            )}
        </>
    )


}

export default Team;