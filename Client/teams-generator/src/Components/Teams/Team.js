
import { useContext } from 'react';
import { Button, Col, Popconfirm, Row, Tooltip } from 'antd';
import { QuestionCircleOutlined } from '@ant-design/icons';
import './Team.css'
import { TeamsContext } from '../../Store/TeamsContext';

function Team(props) {

    function getTeamsExcludingThis(teamId) {
        return props.teams.filter(t => t.teamId != teamId)
    }


    function moveToOtherTeam(fromTeam, toTeam, player) {
        props.onMovePlayer(fromTeam, toTeam, player)
    }

    function removePlayer(fromTeam, player) {
        props.onRemovePlayer(fromTeam, player)
    }

    return (
        <>

            {props.team.players.map(player =>
                <div>
                    <Row >
                        <Col  style={{ width: '100px', 'text-overflow': 'ellipsis', 'white-space': 'nowrap', 'overflow': 'hidden' , margin: '4px' }} flex='auto'>
                            {player.name}
                        </Col>

                        <Col flex='30px'>
                            <Popconfirm title="Remove player" onConfirm={() => { removePlayer(props.team, player) }}
                                description={"Are you sure to remove " + player.name + "?"}
                                icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                                <Tooltip title="Remove Player">
                                    <Button style={{margin: '4px'}} size='small' type="primary" danger>-</Button>
                                </Tooltip>
                            </Popconfirm>
                        </Col>

                        {getTeamsExcludingThis(props.team.teamId).map((team) =>
                            <Popconfirm title="Move player" onConfirm={() => { moveToOtherTeam(props.team, team, player) }}
                                description={"Are you sure to move " + player.name + " to team " + team.teamId}
                                icon={<QuestionCircleOutlined style={{ color: 'red' }} />}>
                                <Tooltip title={"Move Player to Team " + team.teamId}>
                                    <Button style={{margin: '4px'}} size='small' disabled={props.team.players.length === 1} className="move-to-group" >{team.teamId}</Button>
                                </Tooltip>
                            </Popconfirm>
                        )}
                    </Row>
                </div>
            )}
        </>
    )


}

export default Team;